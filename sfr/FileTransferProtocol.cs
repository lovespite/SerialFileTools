using System.Diagnostics;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Text;

namespace sfr;

// ReSharper disable once UnusedType.Global
public class FileTransferProtocol : ProtocolBase
{
    public override string Name => "FTP";
    public override ushort Id => 0x1000;
    public override string DisplayName => "File Transfer Protocol (FTP)";

    public override Encoding Encoding => Encoding.UTF8;

    protected override void BeforeStreamingIn(SerialPort port, ref Meta meta)
    {
        if (IsInvalidFileName(meta))
        {
            StreamStopBy(port);

            throw new Exception("Unexpected file name:" + meta);
        }

        StreamContinue(port);
    }

    protected override FileStream ProcessDataStreamIn(SerialPort port, ref Meta meta)
    {
        var file = Path.Combine(Application.OutputDirectory, meta);

        var blockSize = meta.BlockSize;
        var fs = File.Create(file);

        var buffer = new byte[blockSize];
        int read;
        var totalRead = 0L;

        var sw = Stopwatch.StartNew();

        var retry = 0;
        while ((read = port.ReadAtLeast(buffer, 0, buffer.Length)) > 0)
        {
            if (read != blockSize)
            {
                CConsole.Warn("\nData block incomplete warning:");
                CConsole.Warn($" - Block size mismatch. Expected: {blockSize}, received: {read}");
                CConsole.Warn($" - Waiting for retransmission...{++retry} retry.");
                StreamIncomplete(port);

                // retry this block
                continue;
            }

            fs.Write(buffer, 0, read);

            totalRead += read;
            if (totalRead > meta.Length) totalRead = meta.Length;
            PrintProgress(meta, sw, totalRead);

            StreamContinue(port);
            if (totalRead < meta.Length) continue;
            break;
        }

        // drop padding bytes
        fs.SetLength(meta.Length);

        // flush to disk
        fs.Flush();

        if (retry > 5)
        {
            CConsole.Warn("\nToo many incomplete retries. Consider using a lower baud rate.");
        }

        return fs;
    }

    protected override void AfterStreamingIn(SerialPort port, ref Meta meta, Stream stream)
    {
        var file = Path.Combine(Application.OutputDirectory, meta);

        stream.Seek(0, SeekOrigin.Begin);

        var sha1 = SHA1.HashDataAsync(stream).AsTask();

        var isMatch = sha1.Result.SequenceEqual(meta.SignatureBlock);

        if (isMatch)
        {
            CConsole.Ok("\nFile received successfully.");
            CConsole.Info("  - File: " + file);
        }
        else
        {
            CConsole.Error("\nFile received with error:");
            CConsole.Warn($"  - SHA1 Expected: {Convert.ToHexString(meta.SignatureBlock)}");
            CConsole.Warn($"  -      Received: {Convert.ToHexString(sha1.Result)}");
        }
    }

    protected override void BeforeStreamingOut(SerialPort port, ref Meta meta, Stream stream)
    {
    }

    protected override void ProcessDataStreamOut(SerialPort port, ref Meta meta, Stream stream)
    {
        var buffer = new byte[meta.BlockSize];
        int read;
        var totalRead = 0L;

        var sw = new Stopwatch();
        sw.Start();

        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            totalRead += read;

            // send data block
            port.Write(buffer, 0, buffer.Length);

            // calculate and print progress
            PrintProgress(meta, sw, totalRead);

            var retry = 0;
            int head;
            while ((head = port.ReadByte()) != (int)ByteFlag.Continue)
            {
                if (head == (int)ByteFlag.Incomplete)
                {
                    if (retry == 0) Console.Write("\n");
                    CConsole.Warn($"Block retransmitting requested...{++retry}");

                    // block transfer incomplete, retry
                    port.DiscardOutBuffer();
                    port.Write(buffer, 0, buffer.Length);
                }
                else
                {
                    throw new Exception(Flag.GetErrorMessage((byte)head));
                }
            }
        }

        sw.Stop();
    }

    protected override void AfterStreamingOut(SerialPort port, ref Meta meta, Stream stream)
    {
    }


    private static void PrintProgress(Meta meta, Stopwatch sw, long totalRead)
    {
        // calculate and print progress
        var speed = totalRead / 1024f * 1000 / sw.ElapsedMilliseconds;
        var remainingTime = (meta.Length - totalRead) / 1024f / speed;
        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write(ProgressOf(meta, totalRead, speed, (int)Math.Ceiling(remainingTime)));
    }

    private static string ProgressOf(Meta meta, long totalRead, float speed, int remainingTime)
    {
        return
            ($"{totalRead}/{meta.Length} " +
             $"{totalRead * 100 / meta.Length}%, " +
             $"{speed:F1} KB/s, " +
             $"{remainingTime} s left.").PadRight(Console.BufferWidth - 2);
    }

    private static bool IsInvalidFileName(string name)
    {
        return string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
    }
}