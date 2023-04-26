﻿// See https://aka.ms/new-console-template for more information

using SerialFileTools;

var arg = args.FirstOrDefault();
if (arg == null)
{
    Console.WriteLine("Usage: sfs <file> [port]");
    Console.WriteLine("Note: Start `sfr <dir> [port]` on the other side first.");
    return;
}

var fileName = arg;

var port = args.Skip(1).FirstOrDefault();

var sfs = SerialFileSender.Create(fileName, port);

sfs.Start();