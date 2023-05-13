using System.Reflection;

namespace sfr;

public static class Protocol
{

    private static readonly Dictionary<ushort, ProtocolBase> Protocols = new();

    static Protocol()
    {
        Assembly.GetExecutingAssembly().GetTypes().Where(t => t.IsSubclassOf(typeof(ProtocolBase))).ToList().ForEach(
            t =>
            {
                var p = (ProtocolBase?)Activator.CreateInstance(t);
                if (p is null) return;
                Protocols.Add(p.Id, p);
            });
    }

    public static ProtocolBase? GetProtocol(ushort protocolId)
    {
        return Protocols.TryGetValue(protocolId, out var p) ? p : null;
    }

    public static ProtocolBase? GetProtocol(string name)
    {
        return Protocols.Values.FirstOrDefault(p => p.Name == name);
    }

    public static bool TryGetProtocol(ushort protocolId, out ProtocolBase? protocol)
    {
        return Protocols.TryGetValue(protocolId, out protocol);
    }

    public static bool TryGetProtocol(string name, out ProtocolBase? protocol)
    {
        protocol = Protocols.Values.FirstOrDefault(p => p.Name == name);
        return protocol is not null;
    }

    // quick access
    public static ProtocolBase Ftp => Protocols[0x1000];
}