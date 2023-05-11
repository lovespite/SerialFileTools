namespace sfr;

using LibUsbDotNet;
using LibUsbDotNet.Main;
using LibUsbDotNet.Info;

public class USBHelper
{
    // enum usb devices
    
    public static UsbRegistry[] ListDevices()
    {
        var devices = UsbDevice.AllDevices;
        return devices?.ToArray() ?? Array.Empty<UsbRegistry>();
    }
}