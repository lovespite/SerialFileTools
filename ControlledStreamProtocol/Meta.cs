using System.Runtime.InteropServices;
using System.Text;
using ConsoleExtension;
using Crc16;

namespace ControlledStreamProtocol;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Meta
{
    public static int StructSize => Marshal.SizeOf<Meta>();

    public static Meta Empty() => new() { Head = 0x00 };
    public bool IsEmpty => Head == 0x00;

    public static Meta FromBytes(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length != StructSize)
            throw new Exception(
                $"Bytes length not match. Expected: {StructSize}, Received: {bytes.Length}.");

        var handle = GCHandle.Alloc(bytes.ToArray(), GCHandleType.Pinned);
        var meta = Marshal.PtrToStructure<Meta>(handle.AddrOfPinnedObject());
        handle.Free();
        return meta;
    }

    public long Length => DataStreamLength;

    public ReadOnlyMemory<byte> GetBytes()
    {
        var bytes = new byte[StructSize];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        Marshal.StructureToPtr(this, handle.AddrOfPinnedObject(), false);
        handle.Free();
        return bytes;
    }

    public byte Head; // 1 byte,  0xAA
    public ushort DataLength; // 2 bytes, real length of data stored in DataBytes field
    public int CodePage; // 4 bytes, code page of string data

    public long
        DataStreamLength; // 8 bytes, total length of all data will be transferred not including this Meta struct


    public int BlockSize; // 4 bytes, size of data stream block transferred in one time

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
    public byte[] SignatureBlock; // 20 bytes, SHA1 hash of the whole data stream

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] Reserved; // 8 bytes, reserved

    public ushort BaseVersion; // 2 bytes, base version of protocol
    public ushort Protocol; // 2 bytes, version of protocol used by sender

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
    public byte[] DataBytes; // 512 bytes, string data

    public ushort Crc16; // 2 bytes, CRC16 checksum of DataBytes field

    public Meta(
        long dataStreamLength,
        int blockSize,
        byte[] signatureBlock,
        ushort protocol,
        ushort baseVersion = ProtocolBase.BaseVersion)
    {
        if (signatureBlock.Length != 20)
        {
            throw new ArgumentException("signatureBlock must be 20 bytes.");
        }

        Head = 0xAA;
        DataLength = 0;
        CodePage = 0;

        DataStreamLength = dataStreamLength;
        BlockSize = blockSize;
        SignatureBlock = signatureBlock;
        Reserved = new byte[8];
        BaseVersion = baseVersion;
        Protocol = protocol;
        DataBytes = new byte[512];
        Crc16 = 0;
    }

    public void SetStringData(string stringData, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        var stringDataBytes = encoding.GetBytes(stringData);
        if (stringDataBytes.Length > 512)
        {
            throw new ArgumentException("stringData must be less than 512 bytes.");
        }

        CodePage = encoding.CodePage;
        DataLength = 0;
        Array.Clear(DataBytes, 0, DataBytes.Length);
        Array.Copy(stringDataBytes, DataBytes, stringDataBytes.Length);
        DataLength = (ushort)stringDataBytes.Length;
        Crc16 = Crc16Ccitt.SharedNonZero1.ComputeChecksum(DataBytes, 0, DataLength);
    }

    public string GetStringData(Encoding? encoding = null)
    {
        if (DataLength <= 0)
        {
            return string.Empty;
        }

        if (encoding is null)
        {
            if (CodePage == 0)
            {
                throw new ArgumentException("encoding or CodePage must be specified.");
            }

            encoding = Encoding.GetEncoding(CodePage);
        }

        return encoding.GetString(DataBytes, 0, DataLength);
    }

    public void CheckCrc16()
    {
        bool match;
        if (DataLength <= 0)
            match = Crc16 == 0;
        else
            match = Crc16Ccitt.SharedNonZero1.ComputeChecksum(DataBytes, 0, DataLength) == Crc16;

        if (!match) throw new Exception("CRC16 checksum not match.");
    }

    public void Print()
    {
        var p = Static.Protocol.GetProtocol(Protocol);
        Console.ForegroundColor = ConsoleColor.Green;

        Logger.Info($">> Meta received:");
        Logger.Info($"  -           Head: {Head:X2}");
        Logger.Info($"  -     DataLength: {DataLength}");
        Logger.Info($"  -       CodePage: {CodePage}, {Encoding.GetEncoding(CodePage).EncodingName}");
        Logger.Info($"  -         Length: {Length}");
        Logger.Info($"  -      BlockSize: {BlockSize}");
        Logger.Info($"  -          Block: {Convert.ToHexString(SignatureBlock)}");
        Logger.Info($"  -       Reserved: {Convert.ToHexString(Reserved)}");
        Logger.Info($"  -    BaseVersion: {BaseVersion}");
        Logger.Info($"  -       Protocol: {Protocol:X}");
        Logger.Info($"                    {p?.Name}");
        Logger.Info($"                    {p?.DisplayName}");
        Logger.Info($"  -      DataBytes: {this}");
        Logger.Info($"  -          Crc16: {Crc16:X4}");
        Console.WriteLine();
    }

    public override string ToString()
    {
        return GetStringData();
    }

    public static implicit operator ReadOnlyMemory<byte>(Meta meta) => meta.GetBytes();
    public static implicit operator byte[](Meta meta) => meta.GetBytes().ToArray();
    public static implicit operator string(Meta meta) => meta.ToString();
}