#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Publishes BlueToothAutoConnect and builds the MSI installer.

.DESCRIPTION
    1. Publishes PrivilegedHost (win-x64 self-contained) → publish\PrivilegedHost\
    2. Publishes UserAgent     (win-x64 self-contained) → publish\UserAgent\
    3. Publishes UserAgentCli  (win-x64 self-contained) → publish\UserAgentCli\
    4. Generates WXS fragments with absolute paths + Bitness=always64:
         HostKeyFiles.wxs     — service EXE + ServiceInstall/Control + key DLLs
         AgentKeyFiles.wxs    — UserAgent UI EXE + key files
         AgentCliKeyFiles.wxs — UserAgent CLI EXE + key files
         HostHarvest.wxs      — remaining PrivilegedHost runtime DLLs
         AgentHarvest.wxs     — remaining UserAgent runtime DLLs
         AgentCliHarvest.wxs  — remaining UserAgent CLI runtime DLLs
    5. Builds the Windows x64 MSI.

.PARAMETER Version
    MSI product version in major.minor.build format. Defaults to the VERSION file.

.PARAMETER SkipPublish
    Skip dotnet publish steps (reuse existing publish\ output).

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -Version 1.2.15
    .\build-installer.ps1 -SkipPublish
#>
param(
    [string]$Version,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$root        = $PSScriptRoot

# Read from VERSION file if not explicitly provided
if (-not $Version) {
    $versionFile = Join-Path $root "VERSION"
    if (Test-Path $versionFile) {
        $Version = (Get-Content $versionFile -Raw).Trim()
    }
    if (-not $Version) {
        $Version = "1.0.0"
    }
}

if ($Version -notmatch '^\d{1,5}\.\d{1,5}\.\d{1,5}$') {
    throw "Invalid MSI version '$Version'. Use numeric major.minor.build format, for example 1.0.15."
}

$versionParts = @($Version.Split('.') | ForEach-Object { [int]::Parse($_) })
if ($versionParts[0] -gt 255 -or $versionParts[1] -gt 255 -or $versionParts[2] -gt 65535) {
    throw "Invalid MSI version '$Version'. Major/minor must be 0-255 and build must be 0-65535."
}

$pubDir      = Join-Path $root "publish"
$hostDir     = Join-Path $pubDir "PrivilegedHost"
$agentDir    = Join-Path $pubDir "UserAgent"
$agentCliDir = Join-Path $pubDir "UserAgentCli"
$outDir      = Join-Path $pubDir "Installer"
$instDir     = Join-Path $root  "BlueToothAutoConnect.Installer"

Write-Host "=== Bluetooth Auto Connect Installer Build ===" -ForegroundColor Cyan
Write-Host "Version : $Version"
Write-Host "Root    : $root"

# Regenerate all raster assets from the SVG master before embedding the icons.
$iconGenerator = Join-Path $root "tools\Generate-ApplicationIcons.ps1"
& $iconGenerator -Root $root
if (-not $?) { throw "Application icon generation failed" }

# ── 1. Publish ────────────────────────────────────────────────────────────────
if (-not $SkipPublish) {

  foreach ($publishDirectory in @($hostDir, $agentDir, $agentCliDir)) {
    if (Test-Path $publishDirectory) {
      Remove-Item -Path $publishDirectory -Recurse -Force
    }
  }

    Write-Host "`n--- Publishing PrivilegedHost ---" -ForegroundColor Yellow
    dotnet publish "$root\BlueToothAutoConnect.PrivilegedHost\BlueToothAutoConnect.PrivilegedHost.csproj" `
        -c Release -r win-x64 --self-contained true -o $hostDir /p:Version=$Version
    if ($LASTEXITCODE -ne 0) { throw "PrivilegedHost publish failed" }

    Write-Host "`n--- Publishing UserAgent (WinUI) ---" -ForegroundColor Yellow
    dotnet publish "$root\BlueToothAutoConnect.UserAgent\BlueToothAutoConnect.UserAgent.csproj" `
        -c Release -r win-x64 --self-contained true -o $agentDir /p:Platform=x64 /p:Version=$Version
    if ($LASTEXITCODE -ne 0) { throw "UserAgent publish failed" }

    Write-Host "`n--- Publishing UserAgent CLI ---" -ForegroundColor Yellow
    dotnet publish "$root\BlueToothAutoConnect.UserAgent.Cli\BlueToothAutoConnect.UserAgent.Cli.csproj" `
        -c Release -r win-x64 --self-contained true -o $agentCliDir /p:Platform=x64 /p:Version=$Version
    if ($LASTEXITCODE -ne 0) { throw "UserAgent CLI publish failed" }

} else {
    Write-Host "`n--- Skipping publish ---" -ForegroundColor DarkYellow
    if (-not (Test-Path $hostDir))     { throw "HostDir not found: $hostDir" }
    if (-not (Test-Path $agentDir))    { throw "AgentDir not found: $agentDir" }
    if (-not (Test-Path $agentCliDir)) { throw "AgentCliDir not found: $agentCliDir" }
}

$allowedLanguages = @("en-gb", "en-us")
$cultureDirectoryPattern = '^[a-z]{2,3}(-[a-z0-9]{2,8})*$'
$unsupportedCultureDirectories = Get-ChildItem -LiteralPath $agentDir -Directory |
    Where-Object {
        $_.Name -match $cultureDirectoryPattern -and
        $_.Name.ToLowerInvariant() -notin $allowedLanguages
    }
foreach ($cultureDirectory in $unsupportedCultureDirectories) {
    Remove-Item -LiteralPath $cultureDirectory.FullName -Recurse -Force
}
Write-Host "UI languages: en-GB, en-US"

New-Item -ItemType Directory -Path $outDir -Force | Out-Null

# ── 2. Fragment generator ─────────────────────────────────────────────────────

function New-HarvestFragment {
    param(
        [string]   $SourceDir,
        [string]   $DirectoryRefId,
        [string]   $ComponentGroupId,
        [string[]] $ExcludeFiles,
        [string]   $IdPrefix,
        [string]   $OutputWxs
    )

    $sourceRoot = (Resolve-Path $SourceDir).Path.TrimEnd('\')
    $files = Get-ChildItem -Path $SourceDir -File -Recurse |
             Where-Object {
                 $_.DirectoryName -ne $sourceRoot -or $_.Name -notin $ExcludeFiles
             } |
             Sort-Object FullName
    $rootNode = [pscustomobject]@{
        Name = $null
        Id = $null
        Directories = [System.Collections.Generic.List[object]]::new()
        Files = [System.Collections.Generic.List[object]]::new()
    }
    $directoryIds = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $fileIndex = 0
    $directoryIndex = 0

    foreach ($file in $files) {
        $relativePath = $file.FullName.Substring($sourceRoot.Length).TrimStart('\')
        $relativeDirectory = [System.IO.Path]::GetDirectoryName($relativePath)
        $node = $rootNode

        if ($relativeDirectory) {
            $currentPath = ""
            foreach ($directoryName in $relativeDirectory.Split([System.IO.Path]::DirectorySeparatorChar)) {
                $currentPath = if ($currentPath) { Join-Path $currentPath $directoryName } else { $directoryName }
                $child = $node.Directories | Where-Object { $_.Name -ceq $directoryName } | Select-Object -First 1
                if (-not $child) {
                    $directoryIndex++
                    $child = [pscustomobject]@{
                        Name = $directoryName
                        Id = "${IdPrefix}_D_$directoryIndex"
                        Directories = [System.Collections.Generic.List[object]]::new()
                        Files = [System.Collections.Generic.List[object]]::new()
                    }
                    $node.Directories.Add($child)
                    $directoryIds[$currentPath] = $child.Id
                }
                $node = $child
            }
        }

        $fileIndex++
        $node.Files.Add([pscustomobject]@{
            Id = "${IdPrefix}_F_$fileIndex"
            ComponentId = "${IdPrefix}_C_$fileIndex"
            Source = $file.FullName
        })
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('<?xml version="1.0" encoding="UTF-8"?>')
    $lines.Add('<!-- AUTO-GENERATED by build-installer.ps1 - do not edit -->')
    $lines.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
    $lines.Add('  <Fragment>')
    $lines.Add("    <ComponentGroup Id=`"$ComponentGroupId`" Directory=`"$DirectoryRefId`">")

    function Add-DirectoryNode {
        param(
            [object] $Node,
            [int] $Indent
        )

        $padding = " " * $Indent
        foreach ($directory in $Node.Directories) {
            $lines.Add("$padding<Directory Id=`"$($directory.Id)`" Name=`"$($directory.Name)`">")
            Add-DirectoryNode -Node $directory -Indent ($Indent + 2)
            $lines.Add("$padding</Directory>")
        }
    }

    function Add-ComponentNode {
        param(
            [object] $Node,
            [string] $DirectoryId,
            [int] $Indent
        )

        $padding = " " * $Indent
        foreach ($file in $Node.Files) {
            $guid = [System.Guid]::NewGuid().ToString().ToUpper()
            $lines.Add("$padding<Component Id=`"$($file.ComponentId)`" Guid=`"{$guid}`" Directory=`"$DirectoryId`" Bitness=`"always64`">")
            $lines.Add("$padding  <File Id=`"$($file.Id)`" Source=`"$($file.Source)`" KeyPath=`"yes`" />")
            $lines.Add("$padding</Component>")
        }
        foreach ($directory in $Node.Directories) {
            Add-ComponentNode -Node $directory -DirectoryId $directory.Id -Indent $Indent
        }
    }

    Add-ComponentNode -Node $rootNode -DirectoryId $DirectoryRefId -Indent 6
    $lines.Add("    </ComponentGroup>")

    if ($rootNode.Directories.Count -gt 0) {
        $lines.Add("    <DirectoryRef Id=`"$DirectoryRefId`">")
        Add-DirectoryNode -Node $rootNode -Indent 6
        $lines.Add("    </DirectoryRef>")
    }

    $lines.Add("  </Fragment>")
    $lines.Add("</Wix>")

    ($lines -join "`r`n") | Set-Content $OutputWxs -Encoding UTF8
    Write-Host "  $OutputWxs  ($($files.Count) files, $directoryIndex nested directories)"
}

# ── 3. Generate WXS fragments ─────────────────────────────────────────────────
Write-Host "`n--- Generating WXS fragments ---" -ForegroundColor Yellow

$hostKeyFiles = @(
    "BlueToothAutoConnect.PrivilegedHost.exe",
    "BlueToothAutoConnect.PrivilegedHost.dll",
    "BlueToothAutoConnect.PrivilegedHost.runtimeconfig.json",
    "BlueToothAutoConnect.PrivilegedHost.deps.json",
    "BlueToothAutoConnect.Persistence.dll",
    "BlueToothAutoConnect.PolicyEngine.dll",
    "appsettings.json",
    "appsettings.Development.json"
)

$agentKeyFiles = @(
    "BlueToothAutoConnect.UserAgent.exe",
    "BlueToothAutoConnect.UserAgent.dll",
    "BlueToothAutoConnect.UserAgent.runtimeconfig.json",
    "BlueToothAutoConnect.UserAgent.deps.json",
    "BlueToothAutoConnect.UserAgent.pri"
)

$agentCliKeyFiles = @(
    "BlueToothAutoConnect.UserAgent.Cli.exe",
    "BlueToothAutoConnect.UserAgent.Cli.dll",
    "BlueToothAutoConnect.UserAgent.Cli.runtimeconfig.json",
    "BlueToothAutoConnect.UserAgent.Cli.deps.json"
)

# 3a. HostKeyFiles.wxs — Windows Service + key assemblies
@"
<?xml version="1.0" encoding="UTF-8"?>
<!-- AUTO-GENERATED by build-installer.ps1 - do not edit -->
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <ComponentGroup Id="CG_PrivilegedHost" Directory="ServiceFolder">

      <Component Id="C_HostExe" Guid="B1C2D3E4-F5A6-7B8C-9D0E-1F2A3B4C5D6E" Bitness="always64">
        <File Id="HostExe" Source="$hostDir\BlueToothAutoConnect.PrivilegedHost.exe" KeyPath="yes" />
        <ServiceInstall
          Id="BtAutoConnectService"
          Name="BlueToothAutoConnectHost"
          DisplayName="Bluetooth Auto Connect Privileged Host"
          Description="Handles Bluetooth pairing and unpairing for the Bluetooth Auto Connect User Agent."
          Type="ownProcess"
          Start="auto"
          ErrorControl="normal"
          Account="LocalSystem"
          Vital="yes" />
        <ServiceControl
          Id="BtAutoConnectSvcCtrl"
          Name="BlueToothAutoConnectHost"
          Start="install"
          Stop="both"
          Remove="uninstall"
          Wait="yes" />
      </Component>

      <Component Id="C_HostDll" Guid="B7C8D9E0-F1A2-B3C4-5D6E-7F8A9B0C1D2E" Bitness="always64">
        <File Id="HostDll" Source="$hostDir\BlueToothAutoConnect.PrivilegedHost.dll" KeyPath="yes" />
      </Component>

      <Component Id="C_HostRuntimeConfig" Guid="C2D3E4F5-A6B7-8C9D-0E1F-2A3B4C5D6E7F" Bitness="always64">
        <File Id="HostRuntimeConfig" Source="$hostDir\BlueToothAutoConnect.PrivilegedHost.runtimeconfig.json" KeyPath="yes" />
      </Component>

      <Component Id="C_HostDepsJson" Guid="E4F5A6B7-C8D9-E0F1-2A3B-4C5D6E7F8A9B" Bitness="always64">
        <File Id="HostDepsJson" Source="$hostDir\BlueToothAutoConnect.PrivilegedHost.deps.json" KeyPath="yes" />
      </Component>

      <Component Id="C_HostAppSettings" Guid="D3E4F5A6-B7C8-9D0E-1F2A-3B4C5D6E7F8A" Bitness="always64">
        <File Id="HostAppSettings" Source="$hostDir\appsettings.json" KeyPath="yes" />
      </Component>

      <Component Id="C_HostPersistenceDll" Guid="F5A6B7C8-D9E0-F1A2-3B4C-5D6E7F8A9B0C" Bitness="always64">
        <File Id="HostPersistenceDll" Source="$hostDir\BlueToothAutoConnect.Persistence.dll" KeyPath="yes" />
      </Component>

      <Component Id="C_HostPolicyEngineDll" Guid="A6B7C8D9-E0F1-A2B3-4C5D-6E7F8A9B0C1D" Bitness="always64">
        <File Id="HostPolicyEngineDll" Source="$hostDir\BlueToothAutoConnect.PolicyEngine.dll" KeyPath="yes" />
      </Component>

    </ComponentGroup>
  </Fragment>
</Wix>
"@ | Set-Content "$instDir\HostKeyFiles.wxs" -Encoding UTF8
Write-Host "  $instDir\HostKeyFiles.wxs"

# 3b. AgentKeyFiles.wxs — UserAgent UI EXE + key files
@"
<?xml version="1.0" encoding="UTF-8"?>
<!-- AUTO-GENERATED by build-installer.ps1 - do not edit -->
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <ComponentGroup Id="CG_UserAgent" Directory="UserAgentFolder">

      <Component Id="C_AgentExe" Guid="C8D9E0F1-A2B3-C4D5-6E7F-8A9B0C1D2E3F" Bitness="always64">
        <File Id="AgentExe" Source="$agentDir\BlueToothAutoConnect.UserAgent.exe" KeyPath="yes" />
      </Component>

      <Component Id="C_AgentDll" Guid="D9E0F1A2-B3C4-D5E6-7F8A-9B0C1D2E3F4A" Bitness="always64">
        <File Id="AgentDll" Source="$agentDir\BlueToothAutoConnect.UserAgent.dll" KeyPath="yes" />
      </Component>

      <Component Id="C_AgentRuntimeConfig" Guid="E0F1A2B3-C4D5-E6F7-8A9B-0C1D2E3F4A5B" Bitness="always64">
        <File Id="AgentRuntimeConfig" Source="$agentDir\BlueToothAutoConnect.UserAgent.runtimeconfig.json" KeyPath="yes" />
      </Component>

      <Component Id="C_AgentDepsJson" Guid="F1A2B3C4-D5E6-F7A8-9B0C-1D2E3F4A5B6C" Bitness="always64">
        <File Id="AgentDepsJson" Source="$agentDir\BlueToothAutoConnect.UserAgent.deps.json" KeyPath="yes" />
      </Component>

      <Component Id="C_AgentResourcesPri" Guid="A2B3C4D5-E6F7-A8B9-0C1D-2E3F4A5B6C7D" Bitness="always64">
        <File Id="AgentResourcesPri" Source="$agentDir\BlueToothAutoConnect.UserAgent.pri" KeyPath="yes" />
      </Component>

    </ComponentGroup>
  </Fragment>
</Wix>
"@ | Set-Content "$instDir\AgentKeyFiles.wxs" -Encoding UTF8
Write-Host "  $instDir\AgentKeyFiles.wxs"

# 3c. AgentCliKeyFiles.wxs — UserAgent CLI EXE + key files
@"
<?xml version="1.0" encoding="UTF-8"?>
<!-- AUTO-GENERATED by build-installer.ps1 - do not edit -->
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <ComponentGroup Id="CG_UserAgentCli" Directory="UserAgentCliFolder">

      <Component Id="C_AgentCliExe" Guid="A1B2C3D4-E5F6-7A8B-9C0D-1E2F3A4B5C6D" Bitness="always64">
        <File Id="AgentCliExe" Source="$agentCliDir\BlueToothAutoConnect.UserAgent.Cli.exe" KeyPath="yes" />
      </Component>

      <Component Id="C_AgentCliDll" Guid="B2C3D4E5-F6A7-8B9C-0D1E-2F3A4B5C6D7E" Bitness="always64">
        <File Id="AgentCliDll" Source="$agentCliDir\BlueToothAutoConnect.UserAgent.Cli.dll" KeyPath="yes" />
      </Component>

      <Component Id="C_AgentCliRuntimeConfig" Guid="C3D4E5F6-A7B8-9C0D-1E2F-3A4B5C6D7E8F" Bitness="always64">
        <File Id="AgentCliRuntimeConfig" Source="$agentCliDir\BlueToothAutoConnect.UserAgent.Cli.runtimeconfig.json" KeyPath="yes" />
      </Component>

      <Component Id="C_AgentCliDepsJson" Guid="D4E5F6A7-B89C-0D1E-2F3A-4B5C6D7E8F9A" Bitness="always64">
        <File Id="AgentCliDepsJson" Source="$agentCliDir\BlueToothAutoConnect.UserAgent.Cli.deps.json" KeyPath="yes" />
      </Component>

    </ComponentGroup>
  </Fragment>
</Wix>
"@ | Set-Content "$instDir\AgentCliKeyFiles.wxs" -Encoding UTF8
Write-Host "  $instDir\AgentCliKeyFiles.wxs"

# 3d. Harvest fragments (runtime DLLs, all remaining files)
New-HarvestFragment `
    -SourceDir        $hostDir `
    -DirectoryRefId   "ServiceFolder" `
    -ComponentGroupId "CG_PrivilegedHostHarvested" `
    -ExcludeFiles     $hostKeyFiles `
    -IdPrefix         "H" `
    -OutputWxs        "$instDir\HostHarvest.wxs"

New-HarvestFragment `
    -SourceDir        $agentDir `
    -DirectoryRefId   "UserAgentFolder" `
    -ComponentGroupId "CG_UserAgentHarvested" `
    -ExcludeFiles     $agentKeyFiles `
    -IdPrefix         "A" `
    -OutputWxs        "$instDir\AgentHarvest.wxs"

New-HarvestFragment `
    -SourceDir        $agentCliDir `
    -DirectoryRefId   "UserAgentCliFolder" `
    -ComponentGroupId "CG_UserAgentCliHarvested" `
    -ExcludeFiles     $agentCliKeyFiles `
    -IdPrefix         "AC" `
    -OutputWxs        "$instDir\AgentCliHarvest.wxs"

# ── 4. Build the MSI ──────────────────────────────────────────────────────────
Write-Host "`n--- Building MSI ---" -ForegroundColor Yellow

Remove-Item -Recurse -Force "$instDir\bin", "$instDir\obj" -ErrorAction SilentlyContinue

dotnet build "$instDir\BlueToothAutoConnect.Installer.wixproj" `
    -c Release --no-incremental `
    /p:ProductVersion=$Version `
    /p:OutputName="BlueToothAutoConnect-$Version-Setup"
if ($LASTEXITCODE -ne 0) { throw "MSI build failed" }

$msi = Join-Path $outDir "BlueToothAutoConnect-$Version-Setup.msi"
if (Test-Path $msi) {
    $size = [math]::Round((Get-Item $msi).Length / 1MB, 1)
    Write-Host "`n=== Done ===" -ForegroundColor Green
    Write-Host "MSI : $msi  ($size MB)"
} else {
    Write-Warning "MSI not found at expected path: $msi"
    Get-ChildItem $outDir | Select-Object Name, Length
}
