# BlueToothAutoConnect — Architecture, Directory Structure & Workflow Reference

> **Purpose:** This document is a comprehensive architecture and codebase reference for developers and AI agents (such as Claude, Devin, ChatGPT, Antigravity, GitHub Copilot). It explains the purpose of every folder and key file, inter-process connections, data flows, and operational workflows.

---

## 1. System Overview

**BlueToothAutoConnect** is a Windows .NET 8 application designed to reliably discover, automatically pair, connect, and manage whitelisted Bluetooth (BLE & Classic) devices (such as Bluetooth mice, keyboards, and audio headsets) without requiring user intervention.

### Why Dual-Tier Architecture?
In Windows 10/11:
- Programmatic Bluetooth pairing, unpairing, and hardware adapter operations require elevated administrative or `NT AUTHORITY\SYSTEM` privileges.
- Background device watchers (`Windows.Devices.Enumeration.DeviceWatcher`) and user interfaces must run within an active user's interactive desktop session.

To bridge this, BlueToothAutoConnect is divided into:
1. **Privileged Host (`BlueToothAutoConnect.PrivilegedHost`):** A Windows Service running under `LocalSystem` that performs pairing, unpairing, and GATT connection maintenance.
2. **User Agent Applications (`BlueToothAutoConnect.UserAgent` & `UserAgent.Cli`):** Run in the user interactive session, continuously watch for Bluetooth devices, evaluate policy rules, and send commands to the Privileged Host via an IPC Named Pipe.
3. **Shared Policy & Persistence Layers:** Ensure consistent rule evaluation and persistence across both GUI and CLI components.

```mermaid
graph TD
    subgraph Interactive User Session
        UI[WinUI 3 User Agent<br/>BlueToothAutoConnect.UserAgent.exe]
        CLI[CLI User Agent / Daemon<br/>BlueToothAutoConnect.UserAgent.Cli.exe]
        Watcher[DeviceWatcher<br/>AEP Protocol BLE + Classic]
    end

    subgraph Shared Core DLLs
        PE[Policy Engine<br/>BluetoothPolicyEngine]
        DB_REPO[Persistence Repositories<br/>SQLite EF Core]
    end

    subgraph Windows System Session (LocalSystem)
        SVC[Privileged Host Service<br/>BlueToothAutoConnect.PrivilegedHost.exe]
        BT[BluetoothService<br/>WinRT Windows.Devices]
    end

    subgraph File System & OS
        PIPE([Named Pipe IPC<br/>\\.\pipe\BlueToothAutoConnectPipe])
        DB[(SQLite DB<br/>bluetoothautoconnect.db<br/>%ProgramData%\BlueToothAutoConnect)]
        WinBT[(Windows 11 Bluetooth Stack<br/>WinRT BLE / RFCOMM)]
    end

    UI --> Watcher
    CLI --> Watcher
    Watcher --> PE
    PE --> DB_REPO
    DB_REPO --> DB
    UI -->|IPC Request| PIPE
    CLI -->|IPC Request| PIPE
    PIPE --> SVC
    SVC --> BT
    BT --> WinBT
    SVC --> DB_REPO
```

---

## 2. Directory Structure & Key Files

```
d:\Code\dotnet\BlueToothAutoConnect\
│
├── VERSION                               # Semantic versioning file (e.g. 1.0.4)
├── INSTRUCTIONS.md                       # Architectural design notes & requirements
├── PROJECT_PROGRESS.md                   # Development progress and milestones
├── ARCHITECTURE.md                       # (This file) Complete codebase & workflow reference
├── build-installer.ps1                   # Full release build and WiX MSI packager script
│
├── BlueToothAutoConnect.PrivilegedHost/  # [Windows Service] Runs as LocalSystem
│   ├── Program.cs                        # Windows Service entry point and DI host builder
│   ├── Worker.cs                         # Background service worker loop
│   ├── Bluetooth/
│   │   └── BluetoothService.cs           # WinRT Pairing, GATT connection, unpairing old instances
│   └── IPC/
│       └── IpcServer.cs                  # Named Pipe server listener (\\.\pipe\BlueToothAutoConnectPipe)
│
├── BlueToothAutoConnect.UserAgent/       # [WinUI 3 GUI App] Per-user desktop app
│   ├── App.xaml / App.xaml.cs            # WinUI application lifecycle and DI bootstrap
│   ├── MainWindow.xaml / cs              # Main application window
│   ├── Views/
│   │   └── MainPage.xaml / cs            # Discovered devices, whitelist panel, activity logs
│   ├── ViewModels/
│   │   └── MainViewModel.cs              # UI state, trust/untrust commands, IPC connect triggers
│   └── Services/
│       ├── DeviceWatcherService.cs       # Continuous AEP device enumeration and auto-connect
│       └── IpcClient.cs                  # Named Pipe client for GUI
│
├── BlueToothAutoConnect.UserAgent.Cli/   # [Console & Daemon] Lightweight headless agent
│   ├── Program.cs                        # CLI router (list, whitelist, connect, unpair, daemon)
│   ├── Services/
│   │   ├── CliDeviceWatcher.cs           # Headless DeviceWatcher with auto-connect event loops
│   │   └── CliIpcClient.cs               # Named Pipe IPC client for CLI
│
├── BlueToothAutoConnect.PolicyEngine/    # [Shared Core] Connection evaluation and IPC models
│   ├── Models/
│   │   ├── DeviceInfo.cs                 # Normalized Bluetooth device model
│   │   └── PolicyDecision.cs             # ShouldConnect, IsWhitelisted, Reason
│   ├── Policy/
│   │   ├── IPolicyEngine.cs              # Contract for policy evaluation
│   │   ├── BluetoothPolicyEngine.cs      # Whitelist match, connection checks, backoff logic
│   │   └── RetryPolicy.cs                # Exponential backoff tracking per device
│   └── IPC/
│       ├── IpcRequest.cs                 # Connect, Unpair, Ping, ResetAdapter request DTOs
│       └── IpcResponse.cs                # Success/failure status and messages
│
├── BlueToothAutoConnect.Persistence/     # [Data Layer] SQLite with EF Core
│   ├── Context/
│   │   └── BluetoothDbContext.cs         # SQLite DbContext configured for ProgramData path
│   ├── Entities/
│   │   ├── WhitelistEntry.cs             # Rules (Address, Name, Category, IsActive)
│   │   └── DeviceHistory.cs              # Association history for replacing older devices
│   ├── Repositories/
│   │   ├── IWhitelistRepository.cs       # Whitelist CRUD and rule matching
│   │   ├── WhitelistRepository.cs        # Whitelist SQL queries
│   │   ├── IDeviceHistoryRepository.cs   # Device history tracker
│   │   └── DeviceHistoryRepository.cs    # Device history SQL queries
│   └── ServiceCollectionExtensions.cs    # DI registration for DB and repositories
│
├── BlueToothAutoConnect.Installer/       # [WiX v4 Toolset] Windows Installer (MSI)
│   ├── Product.wxs                       # Core MSI package definition, folders, services
│   ├── HostKeyFiles.wxs                  # PrivilegedHost service installation fragment
│   ├── AgentKeyFiles.wxs                 # WinUI UserAgent installation fragment
│   ├── AgentCliKeyFiles.wxs              # CLI UserAgent installation fragment
│   ├── HostHarvest.wxs                   # Harvested runtime DLLs for Host
│   ├── AgentHarvest.wxs                  # Harvested runtime DLLs for UserAgent
│   └── AgentCliHarvest.wxs               # Harvested runtime DLLs for UserAgent CLI
│
└── BlueToothAutoConnect.Tests/           # Unit tests
    ├── PolicyEngineTests.cs              # Policy decision unit tests
    └── PersistenceTests.cs               # Repository unit tests
```

---

## 3. Component Details & Inter-Connections

### 3.1 Inter-Process Communication (IPC)
- **Transport:** Windows Named Pipes: `\\.\pipe\BlueToothAutoConnectPipe`.
- **Security:** Access control list (ACL) configured in [IpcServer.cs](file:///d:/Code/dotnet/BlueToothAutoConnect/BlueToothAutoConnect.PrivilegedHost/IPC/IpcServer.cs) granting read/write to `NT AUTHORITY\SYSTEM` and `NT AUTHORITY\Authenticated Users`.
- **Protocol:** UTF-8 JSON lines (newline delimited JSON strings) containing [IpcRequest](file:///d:/Code/dotnet/BlueToothAutoConnect/BlueToothAutoConnect.PolicyEngine/IPC/IpcRequest.cs) and [IpcResponse](file:///d:/Code/dotnet/BlueToothAutoConnect/BlueToothAutoConnect.PolicyEngine/IPC/IpcResponse.cs).
- **Supported Operations:**
  - `Ping`: Health check.
  - `Connect`: Requests Host to pair/connect a device ID.
  - `Unpair`: Requests Host to unpair a device ID.
  - `ResetAdapter`: (Reserved) Powers off/on Bluetooth radio.

### 3.2 Database & Persistence
- **Storage:** SQLite file located at `%ProgramData%\BlueToothAutoConnect\bluetoothautoconnect.db`.
- **Shared Access:** Both the Privileged Host service and the User Agent read and write to this database via EF Core with `Pooling=false` to avoid cross-process locking.
- **Whitelist Table (`WhitelistEntries`):** Supports rules matching:
  1. Specific MAC address (e.g. `13:05:aa:00:2e:3a`).
  2. Device Name (e.g. `BT5.2 Mouse`).
  3. Device Category (e.g. `Input.Mouse`, `Communication.Headset.Bluetooth`).

---

## 4. End-to-End Workflows

### 4.1 Device Discovery & Auto-Connect Workflow
1. The user agent (`UserAgent.exe` or `UserAgent.Cli.exe daemon /watch`) initializes [CliDeviceWatcher](file:///d:/Code/dotnet/BlueToothAutoConnect/BlueToothAutoConnect.UserAgent.Cli/Services/CliDeviceWatcher.cs) or [DeviceWatcherService](file:///d:/Code/dotnet/BlueToothAutoConnect/BlueToothAutoConnect.UserAgent/Services/DeviceWatcherService.cs).
2. The watcher queries the Windows Association Endpoint (AEP) subsystem using the Bluetooth Classic and BLE protocol GUIDs:
   - Classic: `{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}`
   - BLE: `{bb7bb05e-5972-42b5-94fc-76eaa7084d49}`
3. When a device is discovered or updated, [BluetoothPolicyEngine](file:///d:/Code/dotnet/BlueToothAutoConnect/BlueToothAutoConnect.PolicyEngine/Policy/BluetoothPolicyEngine.cs) evaluates:
   - Is the device already connected? If yes, skip.
   - Is the device under active retry cooldown? If yes, skip.
   - Does it match any active rule in `WhitelistEntries` (by MAC, Name, or Category)?
   - Is it either `IsConnectable == true` OR already `IsPaired == true`?
4. If approved (`decision.ShouldConnect == true`):
   - The user agent opens the named pipe and transmits `IpcRequest { Action = "Connect", DeviceId = device.Id }`.
   - [IpcServer](file:///d:/Code/dotnet/BlueToothAutoConnect/BlueToothAutoConnect.PrivilegedHost/IPC/IpcServer.cs) receives the request and delegates to [BluetoothService](file:///d:/Code/dotnet/BlueToothAutoConnect/BlueToothAutoConnect.PrivilegedHost/Bluetooth/BluetoothService.cs).
   - If already paired, `BluetoothService` queries the device's uncached GATT table (`bleDevice.GetGattServicesAsync(Uncached)`) to establish and open the active radio connection.
   - If not paired, `BluetoothService` checks for conflicting stale device associations with the same name or MAC, unpairs them, and calls WinRT `PairAsync(ProtectionLevel.None)`.
   - The result is returned over the pipe. On failure, exponential backoff (5s, 10s, 20s, up to 60s) is enforced.

### 4.2 CLI Operations Workflow
The CLI agent ([BlueToothAutoConnect.UserAgent.Cli.exe](file:///d:/Code/dotnet/BlueToothAutoConnect/BlueToothAutoConnect.UserAgent.Cli/Program.cs)) provides direct control:
- `list [/scan]`: Scans for all nearby and paired devices.
- `whitelist [list]`: Displays active whitelist rules.
- `whitelist add --name "BT5.2 Mouse" --category "Input.Mouse"`: Adds a rule.
- `whitelist remove <id | name | mac>`: Removes a rule.
- `connect <mac | deviceId>`: Resolves MAC to the active AEP device ID and triggers connection via Privileged Host.
- `unpair <mac | deviceId>`: Requests Privileged Host to unpair the specified device.
- `daemon [/watch]`: Starts continuous background monitoring and auto-connection.

---

## 5. Build, Package & Installation Workflow

### Build & Packaging
Run the automated Windows x64 build script:
```powershell
.\build-installer.ps1
```
This script executes:
1. `dotnet publish` for `BlueToothAutoConnect.PrivilegedHost` (Self-contained, win-x64).
2. `dotnet publish` for `BlueToothAutoConnect.UserAgent` (Self-contained, win-x64).
3. `dotnet publish` for `BlueToothAutoConnect.UserAgent.Cli` (Self-contained, win-x64).
4. Generates WiX v4 harvest fragments (`HostHarvest.wxs`, `AgentHarvest.wxs`, `AgentCliHarvest.wxs`).
5. Compiles the Windows x64 MSI.

The WinUI application, CLI, and privileged Windows service are supported on Windows x64. The UI package includes English (United States) and English (United Kingdom) language resources.

### Installation
Install the package with administrative privileges:
```powershell
Start-Process msiexec.exe -ArgumentList '/i "publish\Installer\BlueToothAutoConnect-1.0.4-Setup.msi" /qb' -Verb RunAs
```
- Installs binaries to `D:\Program Files\BlueToothAutoConnect\`.
- Registers and starts the Windows Service `BlueToothAutoConnectHost` under `LocalSystem`.
- Creates data directory `%ProgramData%\BlueToothAutoConnect` with write permissions for all authenticated users.
