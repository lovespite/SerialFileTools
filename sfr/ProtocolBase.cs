using System.Diagnostics;
using System.IO.Ports;
using System.Text;

namespace sfr;

public abstract class ProtocolBase
{
    public static readonly byte[] Continue = { (byte)ByteFlag.Continue };
    public static readonly byte[] StopBy = { (byte)ByteFlag.StopBy };
    public static readonly byte[] ProtocolMismatch = { (byte)ByteFlag.ProtocolMismatch };
    public static readonly byte[] ProtocolNotSupported = { (byte)ByteFlag.ProtocolNotSupported };
    public static readonly byte[] Incomplete = { (byte)ByteFlag.Incomplete };

    public static byte[] FeedbackOf(ByteFlag signal)
    {
        return signal switch
        {
            ByteFlag.Continue => Continue,
            ByteFlag.StopBy => StopBy,
            ByteFlag.Incomplete => Incomplete,
            ByteFlag.ProtocolMismatch => ProtocolMismatch,
            ByteFlag.ProtocolNotSupported => ProtocolNotSupported,
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null)
        };
    }

    public const ushort BaseVersion = 0x1F00;

    public abstract string Name { get; }
    public abstract ushort Id { get; }
    public abstract string DisplayName { get; }
    public abstract Encoding Encoding { get; }
    
    // receive
    protected abstract void BeforeStreamingIn(SerialPort port, ref Meta meta);
    protected abstract Stream ProcessDataStreamIn(SerialPort port, ref Meta meta);
    protected abstract void AfterStreamingIn(SerialPort port, ref Meta meta, Stream stream);
    public void Receive(SerialPort port, ref Meta meta)
    {
        BeforeStreamingIn(port, ref meta);
        using var stream = ProcessDataStreamIn(port, ref meta);
        AfterStreamingIn(port, ref meta, stream);
    }

    // send
    protected abstract void BeforeStreamingOut(SerialPort port, ref Meta meta, Stream stream);
    protected abstract void ProcessDataStreamOut(SerialPort port, ref Meta meta, Stream stream);
    protected abstract void AfterStreamingOut(SerialPort port, ref Meta meta, Stream stream);
    
    public void Send(SerialPort port, ref Meta meta, Stream stream)
    {
        BeforeStreamingOut(port, ref meta, stream);
        ProcessDataStreamOut(port, ref meta, stream);
        AfterStreamingOut(port, ref meta, stream);
    }

    protected static void StreamContinue(SerialPort port)
    {
        port.Write(Continue, 0, 1);
    }
    
    public static void StreamStopBy(SerialPort port)
    {
        port.Write(StopBy, 0, 1);
    }
    
    public static void StreamIncomplete(SerialPort port)
    {
        port.Write(Incomplete, 0, 1);
    }
    
    public static void StreamProtocolMismatch(SerialPort port)
    {
        port.Write(ProtocolMismatch, 0, 1);
    }
    
    public static void StreamProtocolNotSupported(SerialPort port)
    {
        port.Write(ProtocolNotSupported, 0, 1);
    }
    
}