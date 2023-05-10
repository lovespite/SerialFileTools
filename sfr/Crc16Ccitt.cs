namespace sfr;

public enum InitialCrcValue { Zeros, NonZero1 = 0xffff, NonZero2 = 0x1D0F }
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
}