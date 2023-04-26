// See https://aka.ms/new-console-template for more information

using System.IO.Ports;
using SerialFileTools;

if (args.Contains("--list-ports"))
{ 
    SerialPort.GetPortNames().ToList().ForEach(Console.WriteLine);
    return;
}

var dir = args.FirstOrDefault();
var port = args.Skip(1).FirstOrDefault();

if (dir == null)
{
    Console.WriteLine("Usage: sfr <dir> [port]");
    return;
}

if (!Directory.Exists(dir))
{
    try
    {
        Directory.CreateDirectory(dir);
    }
    catch (Exception e)
    {
        Console.WriteLine(e.Message);
        return;
    }
}

Console.WriteLine("Listen at :" + port);

var keepListening = args.Contains("--loop");

while (true)
{
    try
    {
        Console.WriteLine("Waiting for sfs-side response...");
        var t = SerialFileReceiver.WaitAt(port);

        t.Wait();

        var result = t.Result;

        if (result.Success)
        {
            Console.WriteLine("File transfer completed.");
            Console.WriteLine($"Temp file: {result.TmpFileName}");
            var tmp = result.TmpFileName;
            var fileName = Path.Combine(dir, result.FileName);
            File.Move(tmp, fileName, args.Contains("--overwrite"));
            Console.WriteLine($"File saved to: {fileName}");
        }
        else
        {
            Console.WriteLine($"File transfer failed: {result.Message}");
        }

        if (!keepListening) break;
    }
    catch (Exception e)
    {
        Console.WriteLine(e); 
    }
}