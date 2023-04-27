dotnet build sfs -c Release --self-contained -o ~/Desktop/SfTools_Win64 -r win-x64
dotnet build sfs -c Release --self-contained -o ~/Desktop/SfTools_Win32 -r win-x86
dotnet build sfs -c Release --self-contained -o ~/Desktop/SfTools_Linux_x64 -r linux-x64
dotnet build sfs -c Release --self-contained -o ~/Desktop/SfTools_Linux_x32 -r linux-x86
dotnet build sfs -c Release --self-contained -o ~/Desktop/SfTools_Linux_arm -r linux-arm
dotnet build sfs -c Release --self-contained -o ~/Desktop/SfTools_Linux_arm64 -r linux-arm64
dotnet build sfs -c Release --self-contained -o ~/Desktop/SfTools_MacOS -r osx-x64
dotnet build sfs -c Release --self-contained -o ~/Desktop/SfTools_MacOS_arm64 -r osx-arm64

dotnet build sfr -c Release --self-contained -o ~/Desktop/SfTools_Win64 -r win-x64
dotnet build sfr -c Release --self-contained -o ~/Desktop/SfTools_Win32 -r win-x86
dotnet build sfr -c Release --self-contained -o ~/Desktop/SfTools_Linux_x64 -r linux-x64
dotnet build sfr -c Release --self-contained -o ~/Desktop/SfTools_Linux_x32 -r linux-x86
dotnet build sfr -c Release --self-contained -o ~/Desktop/SfTools_Linux_arm -r linux-arm
dotnet build sfr -c Release --self-contained -o ~/Desktop/SfTools_Linux_arm64 -r linux-arm64
dotnet build sfr -c Release --self-contained -o ~/Desktop/SfTools_MacOS -r osx-x64
dotnet build sfr -c Release --self-contained -o ~/Desktop/SfTools_MacOS_arm64 -r osx-arm64
