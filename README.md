# Bluetooth Auto Connect

Bluetooth Auto Connect is a Windows x64 desktop application that watches for nearby Bluetooth devices and automatically connects devices that match your trusted-device whitelist. Its WinUI desktop interface lets you manage trusted devices and monitor discovery and connection activity.

## Features

- Discover nearby and previously paired Bluetooth devices.
- Add devices to the whitelist using their Bluetooth address, name, and/or device category.
- Automatically attempt to connect or pair devices that match an active whitelist rule.
- Connect to a discovered device manually.
- See discovery, service status, whitelist, and connection activity in the UI.
- Keep running in the Windows notification area while the window is hidden.
- Receive Windows notifications when a whitelist-matched device connects or disconnects.
- Use the optional command-line interface for scanning and device management.

## Requirements

- 64-bit Windows 10 or Windows 11.
- A working Bluetooth adapter and Windows Bluetooth services.
- The .NET 8 x64 runtime for the MSI prerequisite check.
- Administrator permission to install the application and its privileged Windows service.

The MSI installs the application UI, CLI, and `BlueToothAutoConnectHost` Windows service. The service performs Bluetooth operations that require elevated privileges; the UI runs in your signed-in desktop session.

## Install

Build the **Windows x64** installer from the repository root in PowerShell:

```powershell
.\build-installer.ps1
```

The script creates the MSI at `publish\Installer\BlueToothAutoConnect-<version>-Setup.msi`, using the version in the `VERSION` file. Run the generated installer from PowerShell:

```powershell
$version = (Get-Content .\VERSION -Raw).Trim()
$msi = ".\publish\Installer\BlueToothAutoConnect-$version-Setup.msi"
Start-Process msiexec.exe -ArgumentList "/i `"$msi`"" -Verb RunAs
```

Follow the setup wizard and accept the MIT license. Installation adds the application to the Start menu and registers it to start when you sign in.

The default installation location is:

```text
C:\Program Files\BlueToothAutoConnect\
```

To upgrade, run the newer MSI. To remove the application, use **Installed apps** in Windows Settings or **Programs and Features**.

## Use the desktop app

Launch **Bluetooth Auto Connect** from the Start menu. The main window shows:

- **Discovered Devices** — nearby and paired devices, their connection status, and actions to trust or connect.
- **Trusted Devices (Whitelist)** — active rules and an action to remove a rule.
- **Activity Log** — recent discovery, connection, and service messages.

Select **Trust** on a discovered device to choose which identifying criteria to use. Refresh the device list or whitelist with the corresponding **Refresh** button if discovery or loading reports an error.

### Run in the notification area

- Minimizing the window hides it and shows a notification that the app is still running in the background.
- Clicking the window’s **X** also hides it rather than exiting.
- Double-click the Bluetooth Auto Connect notification-area icon to reopen the window.
- Right-click the icon and choose **Exit** to close the app.
- When a whitelisted device connects or disconnects, the app displays a Windows notification with the device name.

Windows notification settings, Focus assist, or Do not disturb may suppress notifications.

### Diagnostic logs

Use **Open diagnostic logs** in the app to open its log folder. The rolling diagnostic log is stored at:

```text
%LOCALAPPDATA%\BlueToothAutoConnect\diagnostics.log
```

When reporting a problem, include the relevant log entries and the time the problem occurred.

## Optional command-line interface

The CLI is installed at:

```text
C:\Program Files\BlueToothAutoConnect\UserAgentCli\BlueToothAutoConnect.UserAgent.Cli.exe
```

Run it from PowerShell, for example:

```powershell
$cli = 'C:\Program Files\BlueToothAutoConnect\UserAgentCli\BlueToothAutoConnect.UserAgent.Cli.exe'
& $cli --help
& $cli status
& $cli list --timeout 5
& $cli whitelist list
& $cli whitelist add --mac '13:05:aa:00:34:8b' --name 'BT5.2 Mouse'
& $cli whitelist remove 1
& $cli connect '13:05:aa:00:34:8b'
& $cli unpair '13:05:aa:00:34:8b'
```

The CLI shares the application’s whitelist and database. `daemon` / `watch` runs its own background discovery and auto-connect loop; the desktop app already performs discovery while it is running.

## Data and privacy

The shared SQLite database, including whitelist rules and device history, is stored at:

```text
%ProgramData%\BlueToothAutoConnect\bluetooth.db
```

Bluetooth discovery and automatic connection are performed locally through Windows Bluetooth APIs. The application uses a local named pipe to communicate with its privileged Windows service.

## Build from source

Build and package the Windows x64 applications and MSI from the repository root using PowerShell:

```powershell
.\build-installer.ps1
```

The script publishes the privileged service, WinUI desktop application, and CLI as Windows x64 applications, keeps the UI’s English (United States) and English (United Kingdom) resources, and creates the MSI under `publish\Installer\`.

The solution contains:

- `BlueToothAutoConnect.UserAgent` — WinUI desktop application.
- `BlueToothAutoConnect.UserAgent.Cli` — optional command-line client and daemon.
- `BlueToothAutoConnect.PrivilegedHost` — privileged Windows service.
- `BlueToothAutoConnect.AgentCore`, `BlueToothAutoConnect.PolicyEngine`, and `BlueToothAutoConnect.Persistence` — Bluetooth watching, policy, IPC, and data access.
- `BlueToothAutoConnect.Tests` — automated tests.

Run the test suite with:

```powershell
dotnet test .\BlueToothAutoConnect.Tests\BlueToothAutoConnect.Tests.csproj -c Release
```

## License

This project is distributed under the [MIT License](LICENSE).
