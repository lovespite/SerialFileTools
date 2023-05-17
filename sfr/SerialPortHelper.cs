using System.Security.Cryptography;
using ConsoleExtension;
using ControlledStreamProtocol.Exceptions;
using ControlledStreamProtocol.PortStream;
using ControlledStreamProtocol.Static;

namespace sfr;

public static class SerialPortHelper
{
    public static IControlledPortStream Create(string portName, string parameter)
    {
        return SyncPortStream.Create(portName, parameter);
    }

    public static void SendFile(IControlledPortStream sp)
    {
        sp.DiscardOutBuffer();
        sp.DiscardInBuffer();

        var pName = Application.Protocol;

        if (string.IsNullOrEmpty(pName)) pName = Protocol.Default?.Name;

        if (string.IsNullOrEmpty(pName))
        {
            throw new Exception("Protocol not specified. No default protocol available.");
        }

        using var protocol = Protocol.Create(pName, sp);

        Logger.Low("Using protocol: ");
        Logger.Low($"  - Id: {protocol.Id:X}");
        Logger.Low($"  - Name: {protocol.Name}");
        Logger.Low($"  -       {protocol.DisplayName}");
        Logger.Low($"  - Path: {protocol.GetType().Assembly.Location}");

        protocol.Host();

        Logger.Ok("\nPipes closed.");
    }

    private const int MetaHead = (int)ByteFlag.Head;

    public static void Receive(IControlledPortStream cps)
    {
        try
        {
            // open stream
            cps.Open();

            // try to read meta info and create protocol
            using var protocol = ProtocolBase.GetProtocol(cps, MetaHead);

            // protocol established
            Logger.Ok(">> Streaming...");
            protocol.Receive();
            Console.WriteLine();
        }
        catch (ProtocolBaseVersionNotMatchException)
        {
            ProtocolBase.StreamFeedProtocolMismatch(cps);
            throw;
        }
        catch (ProtocolNotImplementedException)
        {
            ProtocolBase.StreamFeedProtocolNotSupported(cps);
            throw;
        }
        catch (ProtocolInitializationException)
        {
            ProtocolBase.StreamStop(cps);
            throw;
        }
        catch (Exception)
        {
            ProtocolBase.StreamStop(cps);
            throw;
        }
        finally
        {
            cps.Close();
        }
    }
}