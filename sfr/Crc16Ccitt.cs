namespace sfr;

public enum InitialCrcValue
{
    Zeros,
    NonZero1 = 0xffff,
    NonZero2 = 0x1D0F
}

public class Crc16Ccitt
{
    const ushort Poly = 4129;
    readonly ushort[] _table = new ushort[256];
    readonly ushort _initialValue = 0;

    public Crc16Ccitt(InitialCrcValue initialValue)
    {
        this._initialValue = (ushort)initialValue;
        for (ushort i = 0; i < _table.Length; i++)
        {
            ushort value = 0;
            ushort temp = i;
            for (byte j = 0; j < 8; j++)
            {
                if (((value ^ temp) & 0x0001) != 0)
                {
                    value = (ushort)((value >> 1) ^ Poly);
                }
                else
                {
                    value >>= 1;
                }

                temp >>= 1;
            }

            _table[i] = value;
        }
    }

    public ushort ComputeChecksum(byte[] bytes, int offset, int count)
    {
        ushort crc = _initialValue;
        for (int i = offset; i < offset + count; i++)
        {
            crc = (ushort)((crc << 8) ^ _table[((crc >> 8) ^ bytes[i]) & 0xff]);
        }

        return crc;
    }

    private const int Byte128K = 128 * 1024;

    public byte[] CalcFile(string file)
    {
        using var fs = File.OpenRead(file);
        var buffer = new Memory<byte>(new byte[Byte128K]);
        var crc = _initialValue;
        while (fs.Read(buffer.Span) > 0)
        {
            crc = (ushort)((crc << 8) ^ _table[((crc >> 8) ^ buffer.Span[0]) & 0xff]);
        } 
        fs.Close();
        return BitConverter.GetBytes(crc);
    }
    
    public static byte[] GetFileCrc16Bytes(string file)
    {
        var crc = new Crc16Ccitt(InitialCrcValue.NonZero1);
        return crc.CalcFile(file);
    }
}