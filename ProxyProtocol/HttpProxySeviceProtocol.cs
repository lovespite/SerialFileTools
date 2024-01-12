using ControlledStreamProtocol ;

namespace ProxyProtocol;

public class HttpProxySeviceProtocol : ProtocolBase
{
    public override byte SignalHeader => throw new NotImplementedException();

    public override string Name => throw new NotImplementedException();

    public override ushort Id => throw new NotImplementedException();

    public override string DisplayName => throw new NotImplementedException();

    public override IReadOnlySet<ushort> CompatibleBaseVersions => throw new NotImplementedException();

    protected override void AfterStreamingIn(Stream stream)
    {
        throw new NotImplementedException();
    }

    protected override void AfterStreamingOut(Stream stream)
    {
        throw new NotImplementedException();
    }

    protected override Stream OpenStreamIn()
    {
        throw new NotImplementedException();
    }

    protected override Stream OpenStreamOut()
    {
        throw new NotImplementedException();
    }

    protected override long ProcessDataStreamIn(Stream stream, ReadOnlyMemory<byte> data)
    {
        throw new NotImplementedException();
    }

    protected override void ProcessDataStreamOut(Stream stream)
    {
        throw new NotImplementedException();
    }
};