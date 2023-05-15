using System.Diagnostics; 
using System.IO.Ports; 
using ControlledStreamProtocol.Exceptions; 

namespace ControlledStreamProtocol;

public abstract class ProtocolBase : IDisposable
{
    public const ushort BaseVersion = 0x1F00;

    private static readonly byte[] Continue = { (byte)ByteFlag.Continue };
    private static readonly byte[] StopBy = { (byte)ByteFlag.StopBy };
    private static readonly byte[] ProtocolMismatch = { (byte)ByteFlag.ProtocolMismatch };
    private static readonly byte[] ProtocolNotSupported = { (byte)ByteFlag.ProtocolNotSupported };
    private static readonly byte[] Incomplete = { (byte)ByteFlag.Incomplete };
    
    public static void CheckBaseVersion(ref Meta meta)
    {
        if (meta.BaseVersion != BaseVersion)
            throw new ProtocolBaseVersionNotMatchException(BaseVersion, meta.BaseVersion);
    }

    protected SerialPort? Port { get; private set; }

    protected Meta StreamMeta;

    public void Bind(SerialPort port, ref Meta meta)
    {
        if (Port is not null) Release();

        Port = port;
        StreamMeta = meta;
    }

    private void Release()
    {
        Port = null;
    }

    private void CheckPort()
    {
        if (Port is null) throw new NullReferenceException("Port is not bound.");
    }

    public static byte[] FeedbackOf(ByteFlag signal)
    {
        return signal switch
        {
            ByteFlag.Continue => Continue,
            ByteFlag.StopBy => StopBy,
            ByteFlag.Incomplete => Incomplete,
            ByteFlag.ProtocolMismatch => ProtocolMismatch,
            ByteFlag.ProtocolNotSupported => ProtocolNotSupported,
            ByteFlag.Head => new[] { (byte)ByteFlag.Head },
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null)
        };
    }

    private Stopwatch _stopwatch = new();
    protected long TimeElapsed => _stopwatch.ElapsedMilliseconds;

    public abstract string Name { get; }
    public abstract ushort Id { get; }
    public abstract string DisplayName { get; } 
    public abstract IReadOnlySet<ushort> CompatibleBaseVersions { get; }
    
    #region Receiving Stream

    // receive
    protected abstract Stream OpenStreamIn();
    protected abstract long ProcessDataStreamIn(Stream stream, ReadOnlyMemory<byte> data);
    protected abstract void AfterStreamingIn(Stream stream);

    public void Receive()
    {
        CheckPort();

        Debug.Assert(Port is not null);
        Debug.Assert(Port.IsOpen);

        ResetStreamFlag();
        using var stream = OpenStreamIn();

        _stopwatch = Stopwatch.StartNew();
  
        long leftBytes;
        do
        {      
            var dataBlock = ReadSerialPort(StreamMeta.BlockSize, out var read);

            ResetStreamFlag();
            leftBytes = ProcessDataStreamIn(stream, dataBlock[..read]);
            CheckStreamFlag(); // check if stream was controlled
            
        } while (leftBytes > 0);

        _stopwatch.Stop();

        ResetStreamFlag();
        AfterStreamingIn(stream);
    }

    #endregion

    #region Sending Stream

    // send
    protected abstract void BeforeStreamingOut(Stream stream);
    protected abstract void ProcessDataStreamOut(Stream stream);
    protected abstract void AfterStreamingOut(Stream stream);

    public void Send(Stream stream)
    {
        CheckPort();
        Debug.Assert(Port is not null);
        Debug.Assert(Port.IsOpen);

        BeforeStreamingOut(stream);
        ProcessDataStreamOut(stream);
        AfterStreamingOut(stream);
    }

    #endregion

    #region Instance Stream Control

    private bool _streamFlagged;

    private void ResetStreamFlag() => _streamFlagged = false;

    private void SetStreamFlag()
    {
        _streamFlagged = true;
    }

    private void CanStreamBeControlled()
    {
        if (_streamFlagged)
        {
            throw new InvalidOperationException(
                "Stream cannot be controlled at this time. " +
                "Please wait for the next call to ProcessDataStreamIn()."
            );
        }
    }

    private void CheckStreamFlag()
    {
        if (!_streamFlagged)
        {
            throw new InvalidOperationException(
                "Stream flag is not set. " +
                "Call one of StreamContinue(), StreamRetry() or StreamStop() " +
                "at the end of ProcessDataStreamIn()."
            );
        }
    }

    protected void StreamContinue()
    {
        CanStreamBeControlled();
        StreamContinue(Port!);
        SetStreamFlag();
    }

    protected void StreamRetry()
    {
        CanStreamBeControlled();
        StreamRetry(Port!);
        SetStreamFlag();
    }

    protected void StreamStop()
    {
        CanStreamBeControlled();
        StreamStop(Port!);
        SetStreamFlag();
    }

    #endregion

    #region Raw SerialPort Read

    protected ReadOnlyMemory<byte> ReadSerialPort(int length, out int read)
    {
        var buffer = new byte[length];
        read = ReadAtLeast(Port!, buffer, 0, length);
        return buffer;
    }

    private static int ReadAtLeast(SerialPort serialPort, byte[] buffer, int offset, int count, int timeout = 500)
    {
        var start = DateTime.Now;
        var read = 0;
        serialPort.ReadTimeout = timeout;
        var span = TimeSpan.FromMilliseconds(timeout);

        while (read < count)
        {
            if (DateTime.Now - start > span) break;
            try
            {
                read += serialPort.Read(buffer, offset + read, count - read);
                start = DateTime.Now;
                if (read == 0) Task.Delay(10).Wait();
            }
            catch (TimeoutException)
            {
                break;
            }
        }

        serialPort.ReadTimeout = SerialPort.InfiniteTimeout;
        return read;
    }

    #endregion

    #region Static Stream Control

    #region Private Static

    // Private static methods
    private static void StreamContinue(SerialPort port)
    {
        port.Write(Continue, 0, 1);
    }

    private static void StreamRetry(SerialPort port)
    {
        port.Write(Incomplete, 0, 1);
    }

    #endregion

    #region Public Static

    // Public static methods

    public static void StreamStop(SerialPort port)
    {
        port.Write(StopBy, 0, 1);
    }

    public static void StreamFeedProtocolMismatch(SerialPort port)
    {
        port.Write(ProtocolMismatch, 0, 1);
    }

    public static void StreamFeedProtocolNotSupported(SerialPort port)
    {
        port.Write(ProtocolNotSupported, 0, 1);
    }

    #endregion

    #endregion

    public void Dispose()
    {
        Release();
    }
}