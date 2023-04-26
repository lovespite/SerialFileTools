using System;
using System.IO;
using System.IO.Ports;

namespace SerialFileTools;

public class ReceiveResult
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; } = 0;
    public bool Success { get; set; } = false;
    public string Message { get; set; } = string.Empty;
    public string TmpFileName { get; set; } = string.Empty;
}

public class SerialFileReceiver
{
    private readonly SerialPort _serialPort;
    private long _fileSize;
    private readonly Stream _fileStream;
    private string _fileName;
    private readonly byte[] _buffer;
    private readonly int _bufferSize;
    private int _readBytes;
    private readonly byte[] _dataPacket;

    public string FileName => _fileName;
    public long FileSize => _fileSize;

    public static Task<ReceiveResult> WaitAt(string? port = null)
    {
        if (port == null)
        {
            var ports = SerialPort.GetPortNames();
            port = ports.FirstOrDefault();
        }

        if (port == null)
        {
            throw new Exception("No serial port found.");
        }

        var tmp = Path.GetTempFileName();


        return Task.Run(() =>
        {
            using var fileStream = new FileStream(tmp, FileMode.Create, FileAccess.Write);
            var sfr = new SerialFileReceiver(port, 115200, fileStream, 1024);
            var result = new ReceiveResult();
            try
            {
                sfr.Start();
                result.Success = true;
                result.FileName = sfr.FileName;
                result.FileSize = sfr._fileSize;
                result.TmpFileName = tmp;
            }
            catch (Exception e)
            {
                result.Message = e.Message;
                result.Success = false;
            }

            return result;
        });
    }

    public SerialFileReceiver(string portName, int baudRate, Stream stream, int readBytes)
    {
        this._fileName = string.Empty;
        this._fileStream = stream;
        this._readBytes = readBytes;
        this._serialPort = new SerialPort(portName, baudRate);
#if DEBUG
        this._serialPort.ReadTimeout = -1;
#else
        this._serialPort.ReadTimeout = 5000;
#endif
        this._bufferSize = 1024; // 1KB
        this._buffer = new byte[_bufferSize];
        this._dataPacket = new byte[_bufferSize + 10]; // 包头(1) + 数据长度(2) + 文件名称(255) + 文件大小(4) + 数据内容(1024) + 校验和(2)
    }

    public void Start()
    {
        try
        {
            _serialPort.Open();
            ReceiveFileInfo();

            _serialPort.ReadTimeout = 5000;
            // print file info
            Console.WriteLine($"File name: {_fileName}");
            Console.WriteLine($"File size: {_fileSize} bytes");

            ReceiveFileData();

            Console.WriteLine("File receive completed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: \n" + ex);
            throw;
        }
        finally
        {
            _serialPort.ReadTimeout = -1; 
            _serialPort.Close();
            _fileStream.Close(); 
        }
    }

    private void ReceiveFileInfo()
    {
        // 接收文件名称和文件大小信息
        byte[] fileNameBytes = new byte[255];
        Array.Clear(_dataPacket, 0, _dataPacket.Length);
        _serialPort.Read(_dataPacket, 0, 262);
        if (_dataPacket[0] != 0xAA)
        {
            throw new Exception("Invalid data packet.");
        }

        int fileNameLength = (_dataPacket[1] << 8) | _dataPacket[2];
        Array.Copy(_dataPacket, 3, fileNameBytes, 0, fileNameLength);
        this._fileName = System.Text.Encoding.ASCII.GetString(fileNameBytes, 0, fileNameLength);
        this._fileSize = (_dataPacket[258] << 24) | (_dataPacket[259] << 16) | (_dataPacket[260] << 8) |
                         _dataPacket[261];

        // 发送确认信息
        byte[] confirmPacket = new byte[] { 0xBB };
        _serialPort.Write(confirmPacket, 0, 1);
    }

    private void ReceiveFileData()
    {
        // 逐个数据包接收文件内容
        int packetCount = (int)Math.Ceiling((double)_fileSize / _bufferSize);
        for (int i = 0; i < packetCount; i++)
        {
            Array.Clear(_dataPacket, 0, _dataPacket.Length);
            _serialPort.Read(_dataPacket, 0, _bufferSize + 10);
            if (_dataPacket[0] != 0xAA)
            {
                throw new Exception("Invalid data packet.");
            }

            int dataLength = (_dataPacket[1] << 8) | _dataPacket[2];
            Array.Copy(_dataPacket, 8, _buffer, 0, dataLength);
            var crc = new Crc16Ccitt(InitialCrcValue.Zeros);
            var checksum = crc.ComputeChecksum(_dataPacket, 0, dataLength + 8);
            if ((_dataPacket[dataLength + 8] != ((checksum >> 8) & 0xFF)) ||
                (_dataPacket[dataLength + 9] != (checksum & 0xFF)))
            {
                throw new Exception("Checksum error.");
            }

            _fileStream.Write(_buffer, 0, dataLength);

            // 发送确认信息
            byte[] confirmPacket = new byte[] { 0xBB };
            _serialPort.Write(confirmPacket, 0, 1);

            // print progress at same place
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write($"Progress: {i + 1}/{packetCount}");
        }
    }
}