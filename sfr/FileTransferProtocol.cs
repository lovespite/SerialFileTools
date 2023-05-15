using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ConsoleExtension;


namespace sfr;

// ReSharper disable once UnusedType.Global
public class FileTransferProtocol : ProtocolBase
{
    public override string Name => "SFTP";
    public override ushort Id => 0x1000;
    public override string DisplayName => "Simple File Transfer Protocol v1.0";
    public override IReadOnlySet<ushort> CompatibleBaseVersions => new HashSet<ushort>
    {
        0x1F00
    };

    protected override Stream OpenStreamIn()
    {
        if (IsInvalidFileName(StreamMeta))
        {
            StreamStop();
            throw new Exception("Unexpected file name:" + StreamMeta);
        }

        var file = Path.Combine(Application.OutputDirectory, StreamMeta.GetStringData());
        var fs = File.Create(file);

        StreamContinue();
        return fs;
    }

    protected override long ProcessDataStreamIn(Stream stream, ReadOnlyMemory<byte> data)
    {
        var shouldRead = StreamMeta.BlockSize;

        if (data.Length != shouldRead)
        {
            CConsole.Warn("\nData block incomplete warning:");
            CConsole.Warn($" - Block size mismatch. Expected: {shouldRead}, received: {data.Length}");
            CConsole.Warn($" - Waiting for retransmission...");
            StreamRetry();
        }
        else
        {
            stream.Write(data.Span);
            PrintProgress(StreamMeta.Length, TimeElapsed, stream.Length);
            StreamContinue();
        }

        return StreamMeta.Length - stream.Length;
    }

    protected override void AfterStreamingIn(Stream stream)
    {
        stream.SetLength(StreamMeta.Length);
        stream.Flush();

        var file = Path.Combine(Application.OutputDirectory, StreamMeta);

        stream.Seek(0, SeekOrigin.Begin);

        var sha1 = SHA1.HashDataAsync(stream).AsTask();

        var isMatch = sha1.Result.SequenceEqual(StreamMeta.SignatureBlock);

        if (isMatch)
        {
            CConsole.Ok("\nFile received successfully.");
            CConsole.Info("  - File: " + file);
        }
        else
        {
            CConsole.Error("\nFile received with error:");
            CConsole.Warn($"  - SHA1 Expected: {Convert.ToHexString(StreamMeta.SignatureBlock)}");
            CConsole.Warn($"  -      Received: {Convert.ToHexString(sha1.Result)}");
        }
    }

    protected override void BeforeStreamingOut(Stream stream)
    {
    }

    protected override void ProcessDataStreamOut(Stream stream)
    {
        var buffer = new byte[StreamMeta.BlockSize];
        int read;
        var totalRead = 0L;

        var sw = new Stopwatch();
        sw.Start();

        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            totalRead += read;

            // send data block
            Port!.Write(buffer, 0, buffer.Length);

            // calculate and print progress
            PrintProgress(StreamMeta.Length, sw, totalRead);

            var retry = 0;
            int head;
            while ((head = Port.ReadByte()) != (int)ByteFlag.Continue)
            {
                if (head == (int)ByteFlag.Incomplete)
                {
                    if (retry == 0) Console.Write("\n");
                    CConsole.Warn($"Block retransmitting requested...{++retry}");

                    // block transfer incomplete, retry
                    Port.DiscardOutBuffer();
                    Port.Write(buffer, 0, buffer.Length);
                }
                else
                {
                    throw new Exception(Flag.GetErrorMessage((byte)head));
                }
            }
        }

        sw.Stop();
    }

    protected override void AfterStreamingOut(Stream stream)
    {
    }


    private static void PrintProgress(long totalLength, Stopwatch sw, long totalRead)
    {
        // calculate and print progress
        var speed = totalRead / 1024f * 1000 / sw.ElapsedMilliseconds;
        var remainingTime = (totalLength - totalRead) / 1024f / speed;
        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write(ProgressOf(totalLength, totalRead, speed, (int)Math.Ceiling(remainingTime)));
    }

    private static void PrintProgress(long totalLength, long timeElapsed, long totalRead)
    {
        // calculate and print progress
        var speed = totalRead / 1024f * 1000 / timeElapsed;
        var remainingTime = (totalLength - totalRead) / 1024f / speed;
        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write(ProgressOf(totalLength, totalRead, speed, (int)Math.Ceiling(remainingTime)));
    }

    private static string ProgressOf(long totalLength, long totalRead, float speed, int remainingTime)
    {
        return
            ($"{totalRead}/{totalLength} " +
             $"{totalRead * 100 / totalLength}%, " +
             $"{speed:F1} KB/s, " +
             $"{remainingTime} s left.").PadRight(Console.BufferWidth - 2);
    }

    private static bool IsInvalidFileName(string name)
    {
        return string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
    }
}