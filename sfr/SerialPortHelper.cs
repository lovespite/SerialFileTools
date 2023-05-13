using System.Diagnostics;
using System.IO.Ports; 
using System.Security.Cryptography;

namespace sfr;

public static class SerialPortHelper
{ 

    public static int GetBlockSize() => Application.BlockSize;

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
            ProtocolId = Protocol.Ftp.Id,
            BaseVersion = ProtocolBase.BaseVersion,
        }.AsMeta();

        fs.Seek(0, SeekOrigin.Begin);
        return meta;
    }

    public static void SendFile(SerialPort sp)
    {
        sp.DiscardOutBuffer();
        sp.DiscardInBuffer();

        var protocol = Protocol.GetProtocol(Application.ProtocolId) ?? Protocol.Ftp;

        var file = Application.FileName;

        using var fs = File.OpenRead(file);

        var meta = GetFileMetaInfo(Path.GetFileName(file), fs);


        var bytes = meta.GetBytes();
        // send file meta info
        sp.Write(bytes.ToArray(), 0, bytes.Length);

        CConsole.Info("\nFile info sent. Waiting for response...");

        var head = sp.ReadByte();

        if (head != 0xBB)
        {
            CConsole.Error("\nError sending file: " + Flag.GetErrorMessage((byte)head));
            return;
        } 

        CConsole.Ok("\nConfirmed. Send file data...");

        protocol.Send(sp, ref meta, fs);

        CConsole.Ok("\nFile sent successfully.");
    }


    private static Meta ReceiveMetaData(SerialPort sp)
    {
        sp.ReadTimeout = SerialPort.InfiniteTimeout;
        while (sp.ReadByte() != (int)ByteFlag.Head)
        {
        }

        var buffer = new byte[Meta.StructSize];
        buffer[0] = (int)ByteFlag.Head;

        var read = sp.ReadAtLeast(buffer, 1, buffer.Length - 1);

        if (read < buffer.Length - 1)
        {
            throw new Exception("Incomplete meta info.");
        }

        var metaInfo = Meta.FromBytes(buffer);

        return metaInfo;
    }

    public static void Receive(SerialPort sp )
    { 
        Meta meta;
        try
        {
            meta = ReceiveMetaData(sp);
            meta.Print();
            meta.CheckCrc16();
        }
        catch
        {
            ProtocolBase.StreamStopBy(sp);
            throw;
        }

        if (meta.BaseVersion != ProtocolBase.BaseVersion)
        {
            CConsole.Error(
                $"Basic protocol version mismatch. Expected: {ProtocolBase.BaseVersion:X}, received: {meta.BaseVersion:X}");

            ProtocolBase.StreamProtocolMismatch(sp);
            return;
        }

        if (!Protocol.TryGetProtocol(meta.Protocol, out var protocol))
        {
            CConsole.Error($"Unsupported protocol: {meta.Protocol:X}");

            ProtocolBase.StreamProtocolNotSupported(sp);
            return;
        }

        CConsole.Ok(">> Receiving..."); 

        protocol?.Receive(sp, ref meta);

        Console.WriteLine();
    }

}