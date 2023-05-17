using System.Diagnostics;
using ControlledStreamProtocol.Exceptions;
using ControlledStreamProtocol.PortStream;
using ControlledStreamProtocol.Static;

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

    private IControlledPortStream? Port { get; set; }

    protected Meta StreamMeta;

    public void Bind(IControlledPortStream port, ref Meta meta)
    {
        if (Port is not null) Release();

        Port = port;
        StreamMeta = meta;
    }

    public void Bind(IControlledPortStream port)
    {
        if (Port is not null) Release();

        Port = port;
    }

    private void Release()
    {
        Port = null;
    }

    private void CheckPort()
    {
        if (Port is null) throw new NullReferenceException("Port is not bound.");
        if (!Port.IsOpen) Port.Open();
    }

    private Stopwatch _stopwatch = new();
    protected long TimeElapsed => _stopwatch.ElapsedMilliseconds;

    public abstract byte SignalHeader { get; }

    public abstract string Name { get; }
    public abstract ushort Id { get; }
    public abstract string DisplayName { get; }
    public abstract IReadOnlySet<ushort> CompatibleBaseVersions { get; }

    #region Receiving Stream

    // receive
    protected abstract Stream OpenStreamIn();
    protected abstract long ProcessDataStreamIn(Stream stream, ReadOnlyMemory<byte> data);
    protected abstract void AfterStreamingIn(Stream stream);

    public virtual void Receive()
    {
        CheckPort();

        Debug.Assert(Port is not null);
        Debug.Assert(Port.IsOpen);

        Port.DiscardOutBuffer();
        Port.DiscardInBuffer();

        ResetStreamFlag();
        using var stream = OpenStreamIn();

        _stopwatch = Stopwatch.StartNew();

        long leftBytes;
        var buffer = new byte[StreamMeta.BlockSize];
        do
        {
            var read = StreamWaitForResponse(buffer);

            ResetStreamFlag();
            leftBytes = ProcessDataStreamIn(stream, buffer.AsMemory(0, read));
            CheckStreamFlag(); // check if stream was controlled
        } while (leftBytes > 0);

        _stopwatch.Stop();

        ResetStreamFlag();
        AfterStreamingIn(stream);
    }

    #endregion

    #region Sending Stream

    // send
    protected virtual Meta BuildMeta() => Meta.Empty();

    protected abstract Stream OpenStreamOut();

    protected abstract void ProcessDataStreamOut(Stream stream);

    protected abstract void AfterStreamingOut(Stream stream);

    public virtual void Host()
    {
        CheckPort();
        Debug.Assert(Port is not null);
        Debug.Assert(Port.IsOpen);

        StreamMeta = BuildMeta();

        if (!StreamMeta.IsEmpty) StreamWriteBlock(StreamMeta);

        using var stream = OpenStreamOut();
        _stopwatch = Stopwatch.StartNew();
        ProcessDataStreamOut(stream);
        _stopwatch.Stop();
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

    protected virtual void StreamContinue()
    {
        CanStreamBeControlled();
        StreamContinue(Port!);
        SetStreamFlag();
    }

    protected virtual void StreamRetry()
    {
        CanStreamBeControlled();
        StreamRetry(Port!);
        SetStreamFlag();
    }

    protected virtual void StreamStop()
    {
        CanStreamBeControlled();
        StreamStop(Port!);
        SetStreamFlag();
    }

    protected virtual ByteFlag StreamWaitForFlag(int msTimeout = 1000)
    {
        Port!.ReadTimeout = msTimeout;
        var flagByte = Port!.ReadByte();
        Port!.ReadTimeout = IControlledPortStream.InfiniteTimeout;
        return (ByteFlag)flagByte;
    }

    protected virtual int StreamWaitForResponse(byte[] buffer, int msTimeout = 200)
    {
        var read = Port!.ReadAtLeast(buffer, msTimeout);
        return read;
    }

    protected void StreamWrite(ReadOnlyMemory<byte> buffer)
    {
        Port!.Write(buffer);
    }

    protected virtual void StreamWriteBlock(ReadOnlyMemory<byte> block)
    {
        if (block.Length != StreamMeta.BlockSize)
            throw new ArgumentOutOfRangeException(nameof(block), block.Length, null);
        StreamWrite(block);
    }

    #endregion

    #region Static Stream Control

    #region Private Static

    // Private static methods
    private static void StreamContinue(IControlledPortStream port)
    {
        port.Write(Continue);
    }

    private static void StreamRetry(IControlledPortStream port)
    {
        port.Write(Incomplete);
    }

    #endregion

    #region Public Static

    // Public static methods

    public static void StreamStop(IControlledPortStream port)
    {
        port.Write(StopBy);
    }

    public static void StreamFeedProtocolMismatch(IControlledPortStream port)
    {
        port.Write(ProtocolMismatch);
    }

    public static void StreamFeedProtocolNotSupported(IControlledPortStream port)
    {
        port.Write(ProtocolNotSupported);
    }

    public static ProtocolBase GetProtocol(IControlledPortStream cps, byte metaHead)
    {
        cps.ReadTimeout = IControlledPortStream.InfiniteTimeout;
        while (cps.ReadByte() != metaHead)
        {
        }

        var buffer = new byte[Meta.StructSize];
        buffer[0] = metaHead;

        var read = cps.ReadAtLeast(buffer, 1, buffer.Length - 1);

        if (read < buffer.Length - 1)
        {
            throw new Exception("Incomplete meta info.");
        }

        var metaInfo = Meta.FromBytes(buffer);

        metaInfo.Print();
        metaInfo.CheckCrc16();
        CheckBaseVersion(ref metaInfo);

        return Protocol.Create(ref metaInfo, cps);
    }

    #endregion

    #endregion

    public void Dispose()
    {
        Release();
    }
}