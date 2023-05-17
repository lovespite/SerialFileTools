using System.Diagnostics;
using System.Reflection;
using ConsoleExtension;
using ControlledStreamProtocol.Exceptions;
using ControlledStreamProtocol.Extensions;
using ControlledStreamProtocol.PortStream;

namespace ControlledStreamProtocol.Static;

public static class Protocol
{
    // quick access
    public static ProtocolBase? Default => Protocols.Values.FirstOrDefault();

    private static readonly Dictionary<ushort, ProtocolBase> Protocols = new();

    private static bool IsProtocolClass(Type type)
    {
        return
            type.IsSubclassOf(typeof(ProtocolBase))
            || (!type.IsAbstract && type.Name.EndsWith("Protocol"));
    }

    public static void LoadProtocolsFromPath(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*Protocol.dll"))
        {
            LoadProtocolsFromAssembly(file);
        }
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
            Logger.Error("Protocol load failed: ");
            Logger.Low($" -  TypeName: {t.Name}:");
            Logger.Low($" -  FullName: {t.FullName}");
            Logger.Low($" -      Path: {t.Assembly.Location}");
            Console.WriteLine();
            return;
        }

        if (!p.CompatibleBaseVersions.Contains(ProtocolBase.BaseVersion))
        {
            Logger.Warn("Protocol base version not compatible: ");
            Logger.Low("  - Attempt to load (ignored): ");
            Logger.Low($"   -  Name: {p.Name}:");
            Logger.Low($"   -        {p.DisplayName}");
            Logger.Low($"   -    ID: {p.Id:X}");
            Logger.Low($"   -  Path: {t.Assembly.Location}");
            Logger.Low($"   -  Base: {ProtocolBase.BaseVersion:X}");
            Logger.Low($"   -  Compatible versions: {string.Join(',', p.CompatibleBaseVersions)}");

            Console.WriteLine();
            return;
        }

        if (Protocols.TryGetValue(p.Id, out var value))
        {
            Logger.Warn("Protocol conflict: ");
            Logger.Ok("  Loaded: ");
            Logger.Ok($"   -  Name: {value.Name}:");
            Logger.Ok($"   -        {value.DisplayName}");
            Logger.Ok($"   -    ID: {value.Id:X}");
            Logger.Ok($"   -  Path: {value.GetType().Assembly.Location}");

            Logger.Low("  Attempt to load (ignored): ");
            Logger.Low($"   -  Name: {p.Name}:");
            Logger.Low($"   -        {p.DisplayName}");
            Logger.Low($"   -    ID: {p.Id:X}");
            Logger.Low($"   -  Path: {t.Assembly.Location}");
            Console.WriteLine();
            return;
        }

        Logger.Ok($"Protocol loaded ({Protocols.Count + 1}) :");
        Logger.Low($" -  Name: {p.Name}:");
        Logger.Low($" -        {p.DisplayName}");
        Logger.Low($" -    ID: {p.Id:X}");
        Logger.Low($" -  Path: {t.Assembly.Location}");
        Console.WriteLine();

        Protocols.Add(p.Id, p);
    } 

    public static ProtocolBase Create(ref Meta meta, IControlledPortStream sp )
    {
        Create(meta.Protocol, out var newProtocol);
        newProtocol.Bind(sp, ref meta);
        return newProtocol;
    }

    public static ProtocolBase Create(string name, IControlledPortStream sp)
    {
        Create(name, out var newProtocol); 
        newProtocol.Bind(sp);
        return newProtocol;
    }

    public static ProtocolBase? GetProtocol(ushort protocolId)
    {
        return Protocols.TryGetValue(protocolId, out var p) ? p : null;
    }

    public static ProtocolBase? GetProtocol(string name)
    {
        return Protocols.Values.FirstOrDefault(p => p.Name == name);
    }

    private static bool TryGetProtocol(ushort protocolId, out ProtocolBase? protocol)
    {
        return Protocols.TryGetValue(protocolId, out protocol);
    }

    private static bool TryGetProtocol(string name, out ProtocolBase? protocol)
    {
        protocol = Protocols.Values.FirstOrDefault(p => p.Name.Equals(name));
        return protocol is not null;
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