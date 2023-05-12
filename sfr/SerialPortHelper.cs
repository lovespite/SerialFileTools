using System.Diagnostics;
using System.IO.Ports;
using System.Security.Cryptography;

namespace sfr;

public static class SerialPortHelper
{
    private static readonly byte[] FeedBack = { 0xBB };
    private static readonly byte[] StopFlag = { 0xFF };
    private static readonly byte[] ProtocolMismatch = { 0xF7 };
    private static readonly byte[] Incomplete = { 0xF8 };

    private static int _blockSize = 1024;
    private static readonly int FileInfoBlockSize = 512;

    public static void SetBlockSize(int size)
    {
        if (size is < 128 or > 40960)
            throw new Exception("Invalid block size `" + size + "`. Must be between 128 and 40960.");
        _blockSize = size;
    }

    public static int GetBlockSize() => _blockSize;


    private static int _transInterval; // ms
    public static void SetTransInterval(int ms) => _transInterval = ms;
    public static int GetTransInterval() => _transInterval;


    public static SerialPort Create(string portName, string parameter)
    {
        var parts = parameter.Split(',');
        var baudRate = int.Parse(parts[0]);

        var dataBits = parts.Length > 1 ? int.Parse(parts[1]) : 8;

        var parity = parts.Length > 2
            ? parts[2] switch
            {
                "N" => Parity.None,
                "E" => Parity.Even,
                "O" => Parity.Odd,
                "M" => Parity.Mark,
                "S" => Parity.Space,
                _ => throw new ArgumentException("Invalid parity.", nameof(parameter))
            }
            : Parity.None;

        var stopBits = parts.Length > 3
            ? parts[3] switch
            {
                "1" => StopBits.One,
                "1.5" => StopBits.OnePointFive,
                "2" => StopBits.Two,
                _ => throw new ArgumentException("Invalid stop bits.", nameof(parameter))
            }
            : StopBits.One;

        return new SerialPort(portName, baudRate, parity, dataBits, stopBits);
    }

    private static void SendMetaInfo(SerialPort sp, string file)
    {
        var fi = new FileInfo(file);

        var metaInfo = new MetaInfo
        {
            Name = fi.Name,
            Length = fi.Length,
            Sha1 = SHA1.HashData(File.ReadAllBytes(file)),
            BlockSize = _blockSize
        };

        var buffer = metaInfo.ToBytes().ToArray();

        sp.Write(buffer, 0, buffer.Length);
    }

    private static MetaInfo? ReceiveMetaInfo(SerialPort sp)
    {
        sp.ReadTimeout = SerialPort.InfiniteTimeout;
        while (sp.ReadByte() != 0xAA)
        {
        }

        var buffer = new byte[FileInfoBlockSize];
        buffer[0] = 0xAA;

        var read = sp.ReadAtLeast(buffer, 1, buffer.Length - 1);
        if (read < buffer.Length - 1) return null;

        var metaInfo = MetaInfo.BytesBuilder.GetMetaInfo(buffer);

        return metaInfo;
    }


    private static void SendFileData(SerialPort sp, string file, bool showDetail = false)
    {
        using var fs = File.OpenRead(file);
        var buffer = new byte[_blockSize];
        int read;
        var totalRead = 0L;
        int head;
        var sw = new Stopwatch();
        sw.Start();
        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            totalRead += read;

            // Send data block
            sp.Write(buffer, 0, buffer.Length);

            if (showDetail) CConsole.Log(Convert.ToHexString(buffer, 0, read));

            // calculate speed
            var speed = totalRead / 1024f * 1000 / sw.ElapsedMilliseconds; // KB/s

            // calculate remaining time
            var remaining = (fs.Length - totalRead) / 1024f; // KB
            var remainingTime = remaining / speed; // seconds 

            // print progress
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(
                $"\r{totalRead}/{fs.Length} {totalRead * 100 / fs.Length}% at {speed:F1} KB/s, remaining {remainingTime:F1} s.");

            if (_transInterval > 0) Task.Delay(_transInterval).Wait();

            var retry = 0;
            while ((head = sp.ReadByte()) != (int)ByteFlag.Continue)
            {
                if (head == (int)ByteFlag.Incomplete)
                {
                    if (retry > 20) throw new Exception("Too many retries.");
                     
                    CConsole.Warn($"\nBlock retransmitting requested...{++retry}"); 
                    
                    // block transfer incomplete, retry
                    sp.DiscardOutBuffer();
                    sp.Write(buffer, 0, buffer.Length);
                }
                else
                {
                    throw new Exception(MetaInfo.GetErrorMessage((byte)head));
                }
            }
        }

        sw.Stop();
    }

    private static string ReceiveFileData(SerialPort sp, string dir, MetaInfo meta, bool showDetail = false)
    {
        var filePathName = Path.Combine(dir, meta.Name);
        using var fs = File.Create(filePathName);
        var buffer = new byte[_blockSize];
        int read;
        var totalRead = 0L;

        var retry = 0;
        while ((read = sp.ReadAtLeast(buffer, 0, buffer.Length)) > 0)
        {
            if (showDetail) Console.WriteLine(Convert.ToHexString(buffer, 0, read));

            if (read != _blockSize)
            {
                CConsole.Warn("\nData block incomplete warning:");
                CConsole.Warn($" - Block size mismatch. Expected: {_blockSize}, received: {read}");
                CConsole.Warn($" - Waiting for retransmission...{++retry} retry.");
                sp.Write(Incomplete, 0, 1);
                
                // retry this block
                continue;
            }

            totalRead += read;
            if (totalRead > meta.Length) totalRead = meta.Length;

            fs.Write(buffer, 0, read);

            if (!showDetail)
            {
                // print progress
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write($"{totalRead}/{meta.Length} {totalRead * 100 / meta.Length}%");
            }

            Task.Delay(_transInterval).Wait();
            sp.Write(FeedBack, 0, 1);

            if (totalRead < meta.Length) continue;

            break;
        }

        // drop padding bytes
        fs.SetLength(meta.Length);
        fs.Flush();
        fs.Close();
        if (retry > 5)
        {
            CConsole.Warn("\nToo many incomplete retries. Consider using a lower baud rate.");
        }

        return filePathName;
    }


    public static void SendFile(SerialPort sp, string file, bool showDetail = false)
    {
        sp.DiscardOutBuffer();
        sp.DiscardInBuffer();

        SendMetaInfo(sp, file);

        Task.Delay(50).Wait();

        CConsole.Info("\nFile info sent. Waiting for response...");

        var head = sp.ReadByte();

        if (head != 0xBB)
        {
            CConsole.Error("\nError sending file: " + head.ToString("X"));
            return;
        }

        Task.Delay(50).Wait();

        CConsole.Ok("\nConfirmed. Ready to send file data...");
        SendFileData(sp, file, showDetail);

        CConsole.Ok("\nFile sent successfully.");
    }

    public static void ReceiveFile(SerialPort sp, string dir, bool showDetail = false)
    {
        var d = Directory.CreateDirectory(dir);
        var metaInfo = ReceiveMetaInfo(sp);

        if (metaInfo == null)
        {
            Console.WriteLine("Error receiving file.");
            sp.Write(StopFlag, 0, 1);
            return;
        }

        if (metaInfo.ProtocolVersion != Protocol.ProtocolVersion)
        {
            CConsole.Error(
                $"Protocol version mismatch. Expected: {Protocol.ProtocolVersion:X}, received: {metaInfo.ProtocolVersion:X}");

            sp.Write(ProtocolMismatch, 0, 1);
            return;
        }

        if (_blockSize != metaInfo.BlockSize)
        {
            CConsole.Warn(
                $"   Warning: block size mismatch. Current: {_blockSize}, received: {metaInfo.BlockSize}.");
            CConsole.Warn($"Change block size to {metaInfo.BlockSize} !");

            _blockSize = metaInfo.BlockSize;
        }

        if (string.IsNullOrWhiteSpace(metaInfo.Name))
        {
            sp.Write(StopFlag, 0, 1);
            Console.WriteLine("Empty file name.");
            return;
        }

        // check file name
        var invalidChars = Path.GetInvalidFileNameChars();
        if (metaInfo.Name.IndexOfAny(invalidChars) >= 0)
        {
            sp.Write(StopFlag, 0, 1);
            Console.WriteLine("Invalid file name.");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($">> {DateTime.Now:HH:mm:ss} Ready to receive file:");
        Console.WriteLine(
            "  - Name: {0}\n  - Size: {1} B\n  - Sign: {2}",
            metaInfo.Name,
            metaInfo.Length,
            Convert.ToHexString(metaInfo.Sha1)
        );
        Console.ResetColor();

        Task.Delay(50).Wait();
        sp.DiscardInBuffer();

        sp.Write(FeedBack, 0, 1);
        Task.Delay(50).Wait();

        var sw = new Stopwatch();
        Console.WriteLine(">> Receiving...");
        sw.Start();
        var f = ReceiveFileData(sp, d.FullName, metaInfo, showDetail);
        sw.Stop();
        Console.WriteLine(
            "\nReceived in {0} s, at {1:F2} KB/s",
            sw.ElapsedMilliseconds / 1000f, (metaInfo.Length / 1024f / sw.ElapsedMilliseconds * 1000f));

        var sha1 = SHA1.HashData(File.ReadAllBytes(f));

        var isMatch = sha1.SequenceEqual(metaInfo.Sha1);

        if (isMatch)
        {
            CConsole.Ok("SHA1 matched. File received successfully.");
        }
        else
        {
            CConsole.Error(
                $"SHA1 mismatch. File received with error. Expected: {Convert.ToHexString(metaInfo.Sha1)}, received: {Convert.ToHexString(sha1)}");
        }

        Console.WriteLine();
    }
}