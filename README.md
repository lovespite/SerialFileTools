# SerialFileTools
cross platform serial port file transfer tool

- Linux amd/amd
- Windows amd/arm
- MacOS amd/arm

# Usage

## Common

```sfs --list-ports``` or ```sfr --list-ports``` list all available serial ports

## Receiver Side

```sfr <dir> [port] [options]```
- dir: directory where files saved to
- port: serial port name
- options
  - ```--loop``` keep listening util exit manually
  - ```--overwrite``` overwrite files in dir
  
## Sender Side

```sfs <file> [port]```
- file: file which is to be sent to the receiving side
- port: serial port name
