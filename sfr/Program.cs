using System.IO.Ports;
using System.Text;
using SerialFileTools;
using sfr;

var modeListPorts = args.Contains("--list-ports") || args.Contains("-l");
var modeShowDetail = args.Contains("--detail") || args.Contains("-d");

var modeBehavior = args.Contains("--send") || args.Contains("-s")
    ? PortMode.Send
    : args.Contains("--receive") || args.Contains("-r")
        ? PortMode.Receive
        : PortMode.Send;

if (modeListPorts)
{
    SerialPort.GetPortNames().ToList().ForEach(Console.WriteLine);
    return;
}

var port = args.FirstOrDefault();

if (string.IsNullOrWhiteSpace(port))
{
    Console.WriteLine("Usage: sfr <port> <--send|--receive> [--parameter=9600,8,N,1] [--file=<file>] [--list-ports]");
    return;
}

if (port.Equals("."))
{
    port = SerialPort.GetPortNames().FirstOrDefault();
    if (string.IsNullOrWhiteSpace(port))
    {
        Console.WriteLine("No serial port found.");
        return;
    }
}

var file = (args
    .Where(a => a.StartsWith("--file="))
    .Select(a => a[7..])
    .FirstOrDefault() ?? args
    .Where(a => a.StartsWith("-f="))
    .Select(a => a[3..])
    .FirstOrDefault()) ?? Path.GetTempFileName();

var parameter = args.Where(a => a.StartsWith("--parameter="))
    .Select(a => a[12..])
    .FirstOrDefault() ?? args
    .Where(a => a.StartsWith("-p="))
    .Select(a => a[3..])
    .FirstOrDefault();

var serialPort = SerialPortHelper.Create(port, parameter);

serialPort.Open();

switch (modeBehavior)
{
    case PortMode.Send:
        UsingSendingMode(serialPort, file);
        break;
    case PortMode.Receive:
        UsingReceivingMode(serialPort, file);
        break;
}

void UsingReceivingMode(SerialPort serialPortInstance, string fileToReceive)
{
    SerialPortHelper.ReceiveFile(serialPortInstance, fileToReceive, modeShowDetail);
}

void UsingSendingMode(SerialPort serialPortInstance, string fileToSend)
{
    SerialPortHelper.SendFile(serialPortInstance, fileToSend, modeShowDetail);
}

// Console.ReadKey();