# Build requirements

These are the requirements for building the Windows x64 application and MSI
from source. They are separate from the .NET runtime requirement for installing
the finished application.

## Required tools

| Requirement | Minimum | How to check | Download / notes |
|---|---|---|---|
| Windows x64 | Windows 10, version 1809 (build 17763) or later | Run `$env:OS` and `[Environment]::Is64BitOperatingSystem` in PowerShell. The second command should return `True`. | The installer and application target Windows x64. |
| .NET SDK | .NET 8 SDK, version 8.0.100 or later | Run `dotnet --list-sdks`. At least one listed SDK must be version 8.0.100 or later. From this repository, `dotnet --version` reports the selected SDK. | [Download the .NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Install the **SDK**, not just the runtime. The SDK includes the `dotnet` CLI and MSBuild. |
| Windows PowerShell | Windows PowerShell 5.1 | Run `$PSVersionTable.PSVersion` and confirm the major version is `5` and minor version is `1`. | Included with supported Windows versions. The build script uses Windows desktop assemblies to generate application icons. |
| NuGet package-source access | Access to `api.nuget.org` or your configured NuGet source | Run `dotnet nuget list source`. | Required for the first restore/build. The project restores its .NET dependencies and WiX SDK/extensions from NuGet. |

Git is only needed to clone the repository and for Git-specific helper scripts.
Visual Studio, the Visual Studio C++ workload, and a separately installed WiX
toolset are not required by the repository's build script.

## Check the requirements

Open Windows PowerShell in the repository root and run:

```powershell
$PSVersionTable.PSVersion
[Environment]::Is64BitOperatingSystem
Get-Command dotnet -ErrorAction SilentlyContinue
dotnet --list-sdks
dotnet --version
dotnet nuget list source
```

If `Get-Command dotnet` returns nothing, or no .NET 8.0.100-or-later SDK is
listed, install the .NET SDK from the download link above, then open a new
PowerShell window and run the checks again. To verify that NuGet restore can
reach the configured package sources, run:

```powershell
dotnet restore .\BlueToothAutoConnect.sln
```

## Build the installer

From the repository root, run:

```powershell
.\build-installer.ps1
```

The script restores/publishes the application projects and builds the MSI into
`publish\Installer\`. The output filename includes the version in the `VERSION`
file. See [README.md](README.md) for installation and usage instructions.
