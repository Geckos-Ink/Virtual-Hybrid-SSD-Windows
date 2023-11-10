# Virtual Hybrid SSD for Windows

## Create an hybrid SSD drive to achieve higher speed on Windows - even if you're penniless like me!

VHSSD is an application written in C# .NET based on [WinFsp](https://winfsp.dev/) library which allows you to merge multiple SSD and HDD to achieve an unique drive which uses SSD drives for caching and HDD drives for permanent file saving.

### Requirements:

- .NET Framework 4.8 ([download here](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48))

- WinFsp drivers ([download here]([Download &middot; WinFsp](https://winfsp.dev/rel/)))

### How works

**Pay attention: in this moment is in beta version. Don't use it to save files without buckups.**

For the moment, VHSSD limits to read the drive.ini file in the current working directory (usually the same directory of the executable) where is contained the information about the current virtual drive.

This is an example of drive.ini file:

```
name myVHSSD
letter X

- SSD:
    letter C
    maxSize 10 GB

- HDD:
    letter E
    maxSize 1000 GB

- HDD:
    letter G
    maxSize 500 GB
```

This example demonstrate the the sections which start with an "-" symbol represent an item of a determined array. This means that you can set multiple SSD or HDD drives.

The total space of the virtual drive is the sum of the max sizes of HDD drives.

### This is just for development

In this moment I don't publish releases because VHSSD is <u>**totally under development**</u> phase, so you have to manually compile the Visual Studio project. Can easily occours virtual drive data distruction, forcing you to reset all environment. To achieve this rapidly, just set the `DebugResetEnv` variable in `Static` class (`MyImplementations.cs`) to `true` to automatically delete all created folder during the start up (remember to set it again to `false`).

The library `winfsp-msil` in the project's references could be found in the installation files of **WinFsp** (in my case at path `C:\Program Files (x86)\WinFsp\bin\winfsp-msil.dll`).

### Developer comments

I hope you enjoy this software and its development! I really appreciate your collaboration. I appreciate also advise about features and how to improve development environment!
