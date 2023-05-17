using System.Security.Cryptography;
using ConsoleExtension;
using ControlledStreamProtocol.Exceptions;
using ControlledStreamProtocol.PortStream;
using ControlledStreamProtocol.Static;

namespace sfr;

public static class SerialPortHelper
{
    public static int GetBlockSize() => Application.BlockSize;

    public static IControlledPortStream Create(string portName, string parameter)
    {
        return SyncPortStream.Create(portName, parameter);
    }

    private static Meta GetFileMetaInfo(string name, Stream fs)
    {
        fs.Seek(0, SeekOrigin.Begin);
        var sha1 = SHA1.HashDataAsync(fs).AsTask();
        sha1.Wait();

        var meta = new FileMetaInfo
        {
            FileName = name,
            Length = fs.Length,
            Sha1 = sha1.Result,
            BlockSize = GetBlockSize(),
            ProtocolId = 0,
            BaseVersion = ProtocolBase.BaseVersion,
        }.AsMeta();

        fs.Seek(0, SeekOrigin.Begin);
        return meta;
    }

    private static Meta GetEmptyMeta()
    {
        return new FileMetaInfo
        {
            FileName = string.Empty,
            Length = 0,
            Sha1 = new byte[20],
            BlockSize = GetBlockSize(),
            ProtocolId = 0,
            BaseVersion = ProtocolBase.BaseVersion,
        }.AsMeta();
    }

    public static void SendFile(IControlledPortStream sp)
    {
        sp.DiscardOutBuffer();
        sp.DiscardInBuffer();

        var file = Application.FileName;

        Meta meta;

        if (string.IsNullOrEmpty(file))
        {
            meta = GetEmptyMeta();
        }
        else
        {
            using var fs = File.OpenRead(file);
            meta = GetFileMetaInfo(Path.GetFileName(file), fs);
            fs.Close();
        }

        // ReSharper disable once AccessToStaticMemberViaDerivedType
        var pName = Application.Protocol;

        if (string.IsNullOrEmpty(pName)) pName = Protocol.Default?.Name;

        if (string.IsNullOrEmpty(pName))
        {
            throw new Exception("Protocol not specified. No default protocol available.");
        }

        Protocol.Create(pName, sp, ref meta, out var protocol);

        Logger.Low("Using protocol: ");
        Logger.Low($"  - Id: {protocol.Id:X}");
        Logger.Low($"  - Name: {protocol.Name}");
        Logger.Low($"  -       {protocol.DisplayName}");
        Logger.Low($"  - Path: {protocol.GetType().Assembly.Location}");

        // send file meta info
        sp.Write(meta);
        
        protocol.Send();

        Logger.Ok("\nPipes closed.");
    }


    private const int MetaHead = (int)ByteFlag.Head;

    private static Meta ReceiveMetaData(IControlledPortStream sp)
    {
        sp.ReadTimeout = IControlledPortStream.InfiniteTimeout;
        while (sp.ReadByte() != MetaHead)
        {
        }

        var buffer = new byte[Meta.StructSize];
        buffer[0] = MetaHead;

        var read = sp.ReadAtLeast(buffer, 1, buffer.Length - 1);

        if (read < buffer.Length - 1)
        {
            throw new Exception("Incomplete meta info.");
        }

        var metaInfo = Meta.FromBytes(buffer);

        return metaInfo;
    }

    public static void Receive(IControlledPortStream sp)
    {
        try
        {
            sp.Open();

            var meta = ReceiveMetaData(sp);
            meta.Print();
            meta.CheckCrc16();

            ProtocolBase.CheckBaseVersion(ref meta);

            Protocol.Create(ref meta, sp, out var protocol);

            Logger.Ok(">> Streaming...");
            protocol.Receive();
            Console.WriteLine();
        }
        catch (ProtocolBaseVersionNotMatchException)
        {
            ProtocolBase.StreamFeedProtocolMismatch(sp);
            throw;
        }
        catch (ProtocolNotImplementedException)
        {
            ProtocolBase.StreamFeedProtocolNotSupported(sp);
            throw;
        }
        catch (ProtocolInitializationException)
        {
            ProtocolBase.StreamStop(sp);
            throw;
        }
        catch (Exception)
        {
            ProtocolBase.StreamStop(sp);
            throw;
        }
        finally
        {
            sp.Close();
        }
    }
}