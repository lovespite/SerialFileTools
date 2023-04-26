using System.Reflection.Metadata;

namespace SerialFileTools;

using System;
using System.IO;
using System.IO.Ports;

/// <summary>
/// Serial File Sender Class
/// </summary>
public class SerialFileSender
{
    private readonly SerialPort _serialPort;
    private readonly string _fileName;
    private readonly long _fileSize;
    private readonly FileStream _fileStream;
    private readonly byte[] _buffer;
    private readonly int _bufferSize;
    private int _readBytes;
    private readonly byte[] _dataPacket;

    public static SerialFileSender Create(string fileName, string? port = null)
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
        
        return new SerialFileSender(port, 115200, fileName);
    }

    public SerialFileSender(string portName, int baudRate, string fileName)
    {
        this._serialPort = new SerialPort(portName, baudRate);
        this._fileName = fileName;
        this._fileSize = new FileInfo(fileName).Length;
        this._fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
        this._bufferSize = 1024; // 1KB
        this._buffer = new byte[_bufferSize];
        this._readBytes = 0;
        this._dataPacket = new byte[_bufferSize + 8]; // 包头(1) + 数据长度(2) + 文件名称(255) + 文件大小(4) + 数据内容(1024) + 校验和(2)
    }

    public void Start()
    {
        try
        {
            _serialPort.Open();
            SendFileInfo();
            SendFileData();
            _serialPort.Close();
            _fileStream.Close();
            Console.WriteLine("File transfer completed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
    }

    private void SendFileInfo()
    {
        // 发送文件名称和文件大小信息
        string fileNameOnly = Path.GetFileName(_fileName);
        byte[] fileNameBytes = System.Text.Encoding.ASCII.GetBytes(fileNameOnly);
        Array.Clear(_dataPacket, 0, _dataPacket.Length);
        _dataPacket[0] = 0xAA;
        _dataPacket[1] = (byte)((fileNameBytes.Length >> 8) & 0xFF);
        _dataPacket[2] = (byte)(fileNameBytes.Length & 0xFF);
        Array.Copy(fileNameBytes, 0, _dataPacket, 3, fileNameBytes.Length);
        _dataPacket[258] = (byte)((_fileSize >> 24) & 0xFF);
        _dataPacket[259] = (byte)((_fileSize >> 16) & 0xFF);
        _dataPacket[260] = (byte)((_fileSize >> 8) & 0xFF);
        _dataPacket[261] = (byte)(_fileSize & 0xFF);
        _serialPort.Write(_dataPacket, 0, 262);

        // 等待接收方确认
        byte[] confirmPacket = new byte[1];
        _serialPort.Read(confirmPacket, 0, 1);
        if (confirmPacket[0] != 0xBB)
        {
            throw new Exception("Failed to receive confirmation.");
        }
    }

    private void SendFileData()
    {
        // 逐个数据包传输文件内容
        int packetCount = (int)Math.Ceiling((double)_fileSize / _bufferSize);
        for (int i = 0; i < packetCount; i++)
        {
            _readBytes = _fileStream.Read(_buffer, 0, _bufferSize);
            Array.Clear(_dataPacket, 0, _dataPacket.Length);
            _dataPacket[0] = 0xAA;
            _dataPacket[1] = (byte)((_readBytes >> 8) & 0xFF);
            _dataPacket[2] = (byte)(_readBytes & 0xFF);
            Array.Copy(_buffer, 0, _dataPacket, 8, _readBytes);
            Crc16Ccitt crc = new Crc16Ccitt(InitialCrcValue.Zeros);
            ushort checksum = crc.ComputeChecksum(_dataPacket, 0, _readBytes + 8);
            _dataPacket[_readBytes + 8] = (byte)((checksum >> 8) & 0xFF);
            _dataPacket[_readBytes + 9] = (byte)(checksum & 0xFF);
            _serialPort.Write(_dataPacket, 0, _readBytes + 10);

            // 等待接收方确认
            byte[] confirmPacket = new byte[1];
            _serialPort.Read(confirmPacket, 0, 1);
            if (confirmPacket[0] != 0xBB)
            {
                throw new Exception("Failed to receive confirmation.");
            }
            
            // print progress at same the place 
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write($"Progress: {i + 1}/{packetCount}");
            
            
        }
    }
}