using System.Diagnostics;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Text;

namespace sfr;

public class FileInfo
{
    public string Name { get; set; } = null!;
    public long Length { get; set; }
    public byte[] Sha1 { get; set; } = null!;
}

public static class SerialPortHelper
{
    private static readonly byte[] FeedBack = { 0xBB };
    private static readonly byte[] StopFlag = { 0xFF };

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


    public static SerialPort Create(string portName, string? parameter = "115200,8,N,1")
    {
        parameter ??= "115200,8,N,1";

        var parts = parameter.Split(',');
        var baudRate = int.Parse(parts[0]);
        var dataBits = int.Parse(parts[1]);
        var parity = parts[2] switch
        {
            "N" => Parity.None,
            "E" => Parity.Even,
            "O" => Parity.Odd,
            "M" => Parity.Mark,
            "S" => Parity.Space,
            _ => throw new ArgumentException("Invalid parity.", nameof(parameter))
        };
        var stopBits = parts[3] switch
        {
            "1" => StopBits.One,
            "1.5" => StopBits.OnePointFive,
            "2" => StopBits.Two,
            _ => throw new ArgumentException("Invalid stop bits.", nameof(parameter))
        };
        return new SerialPort(portName, baudRate, parity, dataBits, stopBits);
    }

    private static void SendFileInfo(SerialPort sp, string file)
    {
        var fileInfo = new System.IO.FileInfo(file);
        var name = fileInfo.Name;
        var size = fileInfo.Length;

        var nameBytes = Encoding.UTF8.GetBytes(name);

        if (nameBytes.Length > FileInfoBlockSize - 49) throw new Exception("File name too long.");

        var sha1Bytes = Crc16Ccitt.GetFileCrc16Bytes(file); // SHA1.HashData(File.ReadAllBytes(file));
        var sizeBytes = BitConverter.GetBytes(size);
        var blockSizeBytes = BitConverter.GetBytes(_blockSize);

        var buffer = new byte[FileInfoBlockSize];
        Array.Clear(buffer);
        buffer[0] = 0xAA;

        Array.Copy(sizeBytes, 0, buffer, 1, sizeBytes.Length);
        Array.Copy(blockSizeBytes, 0, buffer, 9, blockSizeBytes.Length);
        Array.Copy(sha1Bytes, 0, buffer, 17, sha1Bytes.Length);
        Array.Copy(nameBytes, 0, buffer, 49, nameBytes.Length);

        sp.Write(buffer, 0, buffer.Length);
        // Console.WriteLine(Convert.ToHexString(buffer));
    }

    private static void SendFileData(SerialPort sp, string file, bool showDetail = false)
    {
        using var fs = File.OpenRead(file);
        var buffer = new byte[_blockSize];
        var read = 0;
        var totalRead = 0;
        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            totalRead += read;

            // Send data block
            sp.Write(buffer, 0, buffer.Length);

            if (showDetail) Console.WriteLine(Convert.ToHexString(buffer, 0, read));

            // print progress
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write($"\r{totalRead}/{fs.Length} {totalRead * 100 / fs.Length}%");

            if (_transInterval > 0) Task.Delay(_transInterval).Wait();

            if (sp.ReadByte() != 0xBB) throw new Exception("Error sending file.");
        }
    }

    private static FileInfo? ReceiveFileInfo(SerialPort sp)
    {
        while (sp.ReadByte() != 0xAA)
        {
        }

        var buffer = new byte[FileInfoBlockSize];
        buffer[0] = 0xAA;

        var read = sp.ReadAtLeast(buffer, 1, buffer.Length - 1);
        if (read < buffer.Length - 1) return null;

        // Console.WriteLine(Convert.ToHexString(buffer));

        var sizeBytes = new byte[8];
        var blockSizeBytes = new byte[8];
        var sha1Bytes = new byte[2];
        var nameBytes = new byte[FileInfoBlockSize - 49];

        Array.Copy(buffer, 1, sizeBytes, 0, sizeBytes.Length);
        Array.Copy(buffer, 9, blockSizeBytes, 0, blockSizeBytes.Length);
        Array.Copy(buffer, 17, sha1Bytes, 0, sha1Bytes.Length);
        Array.Copy(buffer, 49, nameBytes, 0, nameBytes.Length);

        var size = BitConverter.ToInt64(sizeBytes);
        var blockSize = BitConverter.ToInt32(blockSizeBytes);

        if (_blockSize != blockSize)
        {
            Console.WriteLine(
                $"\u26a0\ufe0f   Warning: block size mismatch. Current: {_blockSize}, received: {blockSize}.");
            Console.WriteLine($"Change block size to {blockSize} !");
            _blockSize = blockSize;
        }

        var sha1 = sha1Bytes;
        var name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

        return new FileInfo
        {
            Name = name,
            Length = size,
            Sha1 = sha1
        };
    }

    private static string ReceiveFileData(SerialPort sp, string dir, FileInfo file, bool showDetail = false)
    {
        var filePathname = Path.Combine(dir, file.Name);
        using var fs = File.Create(filePathname);
        var buffer = new byte[_blockSize];
        var read = 0;
        var totalRead = 0L;

        while ((read = sp.ReadAtLeast(buffer, 0, buffer.Length)) > 0)
        {
            if (showDetail) Console.WriteLine(Convert.ToHexString(buffer, 0, read));

            totalRead += read;
            if (totalRead > file.Length) totalRead = file.Length;

            fs.Write(buffer, 0, read);

            if (!showDetail)
            {
                // print progress
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write($"\r{totalRead}/{file.Length} {totalRead * 100 / file.Length}%");
            }

            Task.Delay(_transInterval).Wait();
            sp.Write(FeedBack, 0, 1);

            if (totalRead < file.Length) continue;

            totalRead = file.Length;
            break;
        }

        // drop padding bytes
        fs.SetLength(file.Length);
        fs.Flush();
        fs.Close();

        return filePathname;
    }


    public static void SendFile(SerialPort sp, string file, bool showDetail = false)
    {
        sp.DiscardOutBuffer();
        sp.DiscardInBuffer();

        SendFileInfo(sp, file);

        Task.Delay(50).Wait();

        var head = sp.ReadByte();

        if (head != 0xBB)
        {
            Console.WriteLine("Error sending file." + head.ToString("X"));
            return;
        }

        Task.Delay(50).Wait();
        SendFileData(sp, file, showDetail);
    }

    public static void ReceiveFile(SerialPort sp, string dir, bool showDetail = false)
    {
        var d = Directory.CreateDirectory(dir);
        var file = ReceiveFileInfo(sp);
        Task.Delay(50).Wait();

        if (file == null)
        {
            Console.WriteLine("Error receiving file.");
            sp.Write(StopFlag, 0, 1);
            return;
        }

        if (string.IsNullOrWhiteSpace(file.Name))
        {
            sp.Write(StopFlag, 0, 1);
            Console.WriteLine("Empty file name.");
            return;
        }

        // check file name
        var invalidChars = Path.GetInvalidFileNameChars();
        if (file.Name.IndexOfAny(invalidChars) >= 0)
        {
            sp.Write(StopFlag, 0, 1);
            Console.WriteLine("Invalid file name.");
            return;
        }

        Console.WriteLine(
            " - Name: {0}\n - Size: {1} B\n - Sign: {2}",
            file.Name,
            file.Length,
            BitConverter.ToString(file.Sha1).Replace("-", "")
        );

        Task.Delay(50).Wait();
        sp.DiscardInBuffer();

        sp.Write(FeedBack, 0, 1);
        Task.Delay(50).Wait();

        var sw = new Stopwatch();
        Console.WriteLine("Receiving...");
        sw.Start();
        var f = ReceiveFileData(sp, d.FullName, file, showDetail);
        sw.Stop();
        Console.WriteLine(
            "\nReceived in {0} s, at {1:F2} KB/s",
            sw.ElapsedMilliseconds / 1000f, (file.Length / 1024f / sw.ElapsedMilliseconds * 1000f));

        var sha1 = Crc16Ccitt.GetFileCrc16Bytes(f); //SHA1.HashData(File.ReadAllBytes(f));

        Console.WriteLine(!sha1.SequenceEqual(file.Sha1)
            ? "\n❌  Error receiving file. Signature mismatch."
            : "\n✅  File received completely.");
    }
}