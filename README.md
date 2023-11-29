# SerialFileTools
cross platform serial port file transfer tool

- Linux amd/amd
- Windows amd/arm
- MacOS amd/arm

# Usage

```
Usage (list ports):
    sfr <--list-ports | -l>

Usage (send):
    sfr <PORT> [options] <file>

Usage (receive):
    sfr <PORT> <--receive | -r> [options] <dir>

Usage (debug mode):
    sfr <PORT> <--debug | -D> [options]
    Debug Options:
        --block-size=<size> | -b=<size> Set block size (default: 2048)
        --debug-view=<text|hex|both> | -V=<text|hex|both>
            Default: both
        --text-encoding=<encoding> | -E=<encoding>
            Only suitable for text view mode.
            Possible values are:
                us-ascii,utf-8,utf-16,utf-32
            Default: us-ascii
        --file=<file> | -f=<file> Where bytes data will be saved to.
            Only suitable for debug mode.
            When specified, result will not be printed to console.

When port is '.', the first available usbserial port will be used.

Options:
    --parameter=<B,D,P,S> | -p=<B,D,P,S>  Set port parameter (default: 1600000,8,N,1)
        B: BaudRate, Possible values are:
            115200, 230400, 460800, etc.
        D: DataBits, number of bits per byte, Possible values are:
            5, 6, 7, 8
        P: Parity, Possible values:
            N: None
            E: Even
            O: Odd
            M: Mark
            S: Space
        S: StopBits, Possible values:
            1: One
            1.5: OnePointFive
            2: Two
    --detail | -d                         Show each block detail
    --keep-open | -k                      Keep port open and listen for next file,
                                          Suitable for receiving mode only.
    --overwrite | -o                      Overwrite existing file
    --send | -s                           Send file to port
    --receive | -r                        Receive file from port
    --protocol=<protocol> | -P=<protocol> Specify protocol to use to send
    --protocol-file=<file> | -F=<file>    Load extra protocol from an external
    --help | -h                           Show this help
```
