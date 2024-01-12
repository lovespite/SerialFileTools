using System.Diagnostics;
using System.Security.Cryptography;
using ConsoleExtension;
using ControlledStreamProtocol;

namespace SftpProtocol;

// ReSharper disable once UnusedType.Global
// ReSharper disable once ClassNeverInstantiated.Global
public class FileTransferProtocol : ProtocolBase
{
    public override byte SignalHeader => (byte)ByteFlag.Head;

    private const int BlockSize = 2048;
    public override string Name => "SFTP";
    public override ushort Id => 0x1000;
    public override string DisplayName => "Simple File Transfer Protocol v1.1";

    private string _fileName = string.Empty;

    public override IReadOnlySet<ushort> CompatibleBaseVersions => new HashSet<ushort>
    {
        0x1F00
    };

    protected override Stream OpenStreamIn()
    {
        var path = GetOutputDir();

        _fileName = Path.Combine(path, StreamMeta);

        if (IsInvalidFileName(StreamMeta))
        {
            StreamStop();
            throw new Exception("Unexpected file name:" + StreamMeta);
        }

        var fs = File.Create(_fileName);

        StreamContinue();
        return fs;
    }

    private static string GetOutputDir()
    {
        var path = AppContext.GetData("OutputDirectory") as string ?? Environment
            .GetCommandLineArgs()
            .Skip(2)
            .LastOrDefault(a => !a.StartsWith('-'));
        return path ?? Environment.CurrentDirectory;
    }


    private static string GetFileName()
    {
        var path = AppContext.GetData("FileName") as string;
        return path ?? string.Empty;
    }

    protected override long ProcessDataStreamIn(Stream stream, ReadOnlyMemory<byte> data)
    {
        var shouldRead = StreamMeta.BlockSize;

        if (data.Length == 0)
        {
            StreamStop();
            return 0;
        }

        if (data.Length != shouldRead)
        {
            Logger.Warn("\nData block incomplete warning:");
            Logger.Warn($" - Block size mismatch. Expected: {shouldRead}, received: {data.Length}");
            Logger.Warn($" - Waiting for retransmission...");

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

        stream.Seek(0, SeekOrigin.Begin);

        var sha1 = SHA1.HashData(stream);

        var isMatch = sha1.SequenceEqual(StreamMeta.SignatureBlock);

        if (isMatch)
        {
            Logger.Ok("\nFile received successfully.");
            Logger.Info("  - File: " + _fileName);
            Logger.Info("  - SHA1: " + Convert.ToHexString(sha1));
        }
        else
        {
            Logger.Error("\nFile received with error:");
            Logger.Warn($"  - SHA1 Expected: {Convert.ToHexString(StreamMeta.SignatureBlock)}");
            Logger.Warn($"  -      Received: {Convert.ToHexString(sha1)}");
        }
    }

    protected override Meta BuildMeta()
    {
        var file = GetFileName();
        if (!File.Exists(file)) throw new FileNotFoundException(file);

        using var fs = File.OpenRead(file);
        var fLen = fs.Length;
        var sha1 = SHA1.HashData(fs);
        fs.Close();

        var meta = new Meta(fLen, BlockSize, sha1, Id);
        meta.SetStringData(Path.GetFileName(file));

        return meta;
    }

    protected override void StreamWriteBlock(ReadOnlyMemory<byte> block)
    {
        StreamWrite(block);
    }

    protected override Stream OpenStreamOut()
    {
        Logger.Info("\nWaiting for response...");

        var f = StreamWaitForFlag();

        if (f != ByteFlag.Continue)
        {
            throw new Exception("Error: " + Flag.GetErrorMessage(f));
        }

        Logger.Ok("\n>> Streaming...");

        var file = GetFileName();
        Debug.Assert(File.Exists(file));
        return File.OpenRead(file);
    }

    protected override void ProcessDataStreamOut(Stream stream)
    {
        var buffer = new byte[StreamMeta.BlockSize].AsMemory();
        int read;
        var totalRead = 0L;

        while ((read = stream.Read(buffer.Span)) > 0)
        {
            totalRead += read;

            // send data block
            StreamWriteBlock(buffer);

            var retry = 0;
            do
            {
                var flag = StreamWaitForFlag(-1);
                if (flag == ByteFlag.Continue) break;
                if (flag == ByteFlag.Incomplete)
                {
                    // block transfer incomplete, retry
                    StreamWriteBlock(buffer);
                    if (retry == 0) Console.Write("\n");
                    Logger.Warn($"Block retransmitting requested...{++retry}");
                }
                else if (flag == ByteFlag.StopBy)
                {
                    throw new Exception(Flag.GetErrorMessage(flag));
                }
                else
                {
                    throw new Exception("Unexpected flag: " + flag);
                }
            } while (true);

            // calculate and print progress
            PrintProgress(StreamMeta.Length, TimeElapsed, totalRead);
        }
    }

    protected override void AfterStreamingOut(Stream stream)
    {
        stream.Close();
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