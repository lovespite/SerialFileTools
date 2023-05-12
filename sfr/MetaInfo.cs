using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;

namespace sfr;

public class MetaInfo
{
    public ushort ProtocolVersion { get; set; } = Protocol.ProtocolVersion;
    public string Name { get; set; } = null!;
    public long Length { get; set; }
    public byte[] Sha1 { get; set; } = null!;
    public int BlockSize { get; set; }

    public ReadOnlyMemory<byte> ToBytes()
    {
        return BytesBuilder.GetMetaInfoBytes(this);
    }

    public static class BytesBuilder
    {
        private const int FileInfoBlockSize = 512;

        public static ReadOnlyMemory<byte> GetMetaInfoBytes(MetaInfo metaInfo)
        {
            var name = metaInfo.Name;
            var size = metaInfo.Length;
            var pVersion = metaInfo.ProtocolVersion;

            var nameBytes = Encoding.UTF8.GetBytes(name);

            if (nameBytes.Length > FileInfoBlockSize - 49) throw new Exception("File name too long.");

            var sha1Bytes = metaInfo.Sha1; // 20 bytes
            var sizeBytes = BitConverter.GetBytes(size); // 8 bytes
            var blockSizeBytes = BitConverter.GetBytes(metaInfo.BlockSize); // 4 bytes
            var pVersionBytes = BitConverter.GetBytes(pVersion); // 2 bytes

            var buffer = new byte[FileInfoBlockSize];
            Array.Clear(buffer);
            buffer[0] = 0xAA;

            Array.Copy(sizeBytes, 0, buffer, 1, sizeBytes.Length);
            Array.Copy(blockSizeBytes, 0, buffer, 9, blockSizeBytes.Length);
            Array.Copy(sha1Bytes, 0, buffer, 17, sha1Bytes.Length);
            Array.Copy(pVersionBytes, 0, buffer, 45, pVersionBytes.Length);
            Array.Copy(nameBytes, 0, buffer, 49, nameBytes.Length);

            return new Memory<byte>(buffer);
        }

        public static MetaInfo GetMetaInfo(ReadOnlyMemory<byte> fileInfoBytes)
        {
            var buffer = fileInfoBytes.ToArray();
            var sizeBytes = new byte[8];
            var blockSizeBytes = new byte[4];
            var sha1Bytes = new byte[20];
            var pVersionBytes = new byte[2];
            var nameBytes = new byte[buffer.Length - 49];


            Array.Copy(buffer, 1, sizeBytes, 0, sizeBytes.Length);
            Array.Copy(buffer, 9, blockSizeBytes, 0, blockSizeBytes.Length);
            Array.Copy(buffer, 17, sha1Bytes, 0, sha1Bytes.Length);
            Array.Copy(buffer, 45, pVersionBytes, 0, pVersionBytes.Length);
            Array.Copy(buffer, 49, nameBytes, 0, nameBytes.Length);

            var size = BitConverter.ToInt64(sizeBytes);
            var blockSize = BitConverter.ToInt32(blockSizeBytes);
            var name = Encoding.UTF8.GetString(nameBytes);
            var pVersion = BitConverter.ToUInt16(pVersionBytes);

            return new MetaInfo
            {
                Name = name.TrimEnd('\0'),
                Length = size,
                BlockSize = blockSize,
                Sha1 = sha1Bytes,
                ProtocolVersion = pVersion,
            };
        }
    }

    public static string GetErrorMessage(byte signal)
    {
        switch (signal)
        {
            case (byte)ByteFlag.Continue:
                return "Continue.";
            case (byte)ByteFlag.StopBy:
                return "Stopped by the other side. Typically, the file info in metadata is invalid.";
            case (byte)ByteFlag.Incomplete:
                return "Incomplete data block.";
            case (byte)ByteFlag.ProtocolMismatch:
                return "Protocol mismatch. The other side is using a different protocol.";
            default:
                return "Unknown error.";
        }
    }
}