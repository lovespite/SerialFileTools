namespace ControlledStreamProtocol.Exceptions;

public class ProtocolNotImplementedException : Exception
{
    public ProtocolNotImplementedException(ushort protocolId)
        : base($"Protocol is not implemented. Protocol ID: {protocolId:X}")
    {
    }

    public ProtocolNotImplementedException(string name)
        : base("Protocol is not implemented. Protocol name: " + name)
    {
    }
}

public class ProtocolInitializationException : Exception
{
    public ProtocolInitializationException(ushort protocolId)
        : base($"Protocol initialization failed. Protocol ID: {protocolId:X}")
    {
    }
}

public class ProtocolBaseVersionNotMatchException : Exception
{
    public ProtocolBaseVersionNotMatchException(ushort expected, ushort received)
        : base($"Protocol base version not match. Expected: {expected:X}, received: {received:X}")
    {
    }
}