using System.Diagnostics;
using System.IO.Ports;
using System.Reflection;
using ConsoleExtension;
using ControlledStreamProtocol.Exceptions;
using ControlledStreamProtocol.Extensions;

namespace ControlledStreamProtocol.Static;

public static class Protocol
{
    // quick access
    public static ProtocolBase Sftp => Protocols[0x1000];

    private static readonly Dictionary<ushort, ProtocolBase> Protocols = new();

    private static bool IsProtocolClass(Type type)
    {
        return
            type.IsSubclassOf(typeof(ProtocolBase))
            || (!type.IsAbstract && type.Name.EndsWith("Protocol"));
    }

    public static void LoadProtocolsFromAssembly(Assembly assembly)
    {
        assembly
            .GetTypes()
            .Where(IsProtocolClass)
            .ToList()
            .ForEach(LoadProtocol);
    }

    public static void LoadProtocolsFromAssembly(string file)
    {
        LoadProtocolsFromAssembly(Assembly.LoadFile(file));
    }

    public static void LoadProtocolsFromAssembly(ReadOnlyMemory<byte> data)
    {
        LoadProtocolsFromAssembly(Assembly.Load(data.ToArray()));
    }

    public static void LoadProtocolsFromAssembly(Stream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        LoadProtocolsFromAssembly(stream.ReadAllBytes());
    }

    private static void LoadProtocol(Type t)
    {
        var p = (ProtocolBase?)Activator.CreateInstance(t);
        if (p is null)
        {
            CConsole.Error("Protocol load failed: ");
            CConsole.Low($" -  TypeName: {t.Name}:");
            CConsole.Low($" -  FullName: {t.FullName}");
            Console.WriteLine();
            return;
        }

        if (!p.CompatibleBaseVersions.Contains(ProtocolBase.BaseVersion))
        {
            CConsole.Warn("Protocol base version not compatible: ");
            CConsole.Low("  - Attempt to load (ignored): ");
            CConsole.Low($"   -  Name: {p.Name}:");
            CConsole.Low($"   -        {p.DisplayName}");
            CConsole.Low($"   -    ID: {p.Id:X}");
            CConsole.Low($"   -  Base: {ProtocolBase.BaseVersion:X}");
            Console.WriteLine();
        }

        if (Protocols.TryGetValue(p.Id, out var value))
        {
            CConsole.Warn("Protocol conflict: ");
            CConsole.Ok("  - Loaded: ");
            CConsole.Ok($"   -  Name: {value.Name}:");
            CConsole.Ok($"   -        {value.DisplayName}");
            CConsole.Ok($"   -    ID: {value.Id:X}");

            CConsole.Low("  - Attempt to load (ignored): ");
            CConsole.Low($"   -  Name: {p.Name}:");
            CConsole.Low($"   -        {p.DisplayName}");
            CConsole.Low($"   -    ID: {p.Id:X}");
            Console.WriteLine();
        }

        CConsole.Ok($"Protocol loaded ({Protocols.Count + 1}) :");
        CConsole.Low($" -  Name: {p.Name}:");
        CConsole.Low($" -        {p.DisplayName}");
        CConsole.Low($" -    ID: {p.Id:X}");
        Console.WriteLine();

        Protocols.Add(p.Id, p);
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
        protocol = Protocols.Values.FirstOrDefault(p => p.Name.Equals(name));
        return protocol is not null;
    }

    public static void Create(ref Meta meta, SerialPort sp, out ProtocolBase newProtocol)
    {
        Create(meta.Protocol, out newProtocol);
        newProtocol.Bind(sp, ref meta);
    }

    public static void Create(string name, SerialPort sp, ref Meta meta, out ProtocolBase newProtocol)
    {
        Create(name, out newProtocol);
        meta.Protocol = newProtocol.Id;
        newProtocol.Bind(sp, ref meta);
    }

    private static void Create(ushort protocolId, out ProtocolBase newProtocol)
    {
        if (!TryGetProtocol(protocolId, out var protocol))
            throw new ProtocolNotImplementedException(protocolId);

        Debug.Assert(protocol is not null);

        var protocolBase = Activator.CreateInstance(protocol.GetType()) as ProtocolBase;

        newProtocol = protocolBase ?? throw new ProtocolInitializationException(protocol.Id);
    }

    private static void Create(string name, out ProtocolBase newProtocol)
    {
        if (!TryGetProtocol(name, out var protocol))
            throw new ProtocolNotImplementedException(name);

        Debug.Assert(protocol is not null);

        var protocolBase = Activator.CreateInstance(protocol.GetType()) as ProtocolBase;

        newProtocol = protocolBase ?? throw new ProtocolInitializationException(protocol.Id);
    }
}