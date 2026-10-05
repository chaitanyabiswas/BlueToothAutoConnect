# Bluetooth Auto Connect - Project Progress & Roadmap

## Project Overview
Windows Bluetooth automatic connection system with privileged service and user agent architecture.

**Last Updated:** 2026-09-25  
**Status:** MVP Implementation Complete — Ready for Integration Testing

---

## Architecture Components

### 1. Privileged Host (Windows Service)
- **Path:** `BlueToothAutoConnect.PrivilegedHost/`
- **Status:** 🟢 Core implementation complete
- **Implemented:**
  - [x] Windows Service configuration (`AddWindowsService`)
  - [x] Named pipe IPC server (`IpcServer.cs`) with Windows ACL (AuthenticatedUsers + LocalSystem)
  - [x] `ConnectToDevice` IPC handler → calls `BluetoothService.PairDeviceAsync`
  - [x] `UnpairDevice` IPC handler → calls `BluetoothService.UnpairDeviceAsync`
  - [x] Replace-old-device logic: address match → 10s stability wait → unpair old → update history
  - [x] `Ping` handler for health checks
  - [x] DB auto-created at `%ProgramData%\BlueToothAutoConnect\bluetooth.db`
  - [ ] Adapter reset endpoint
  - [ ] Security: validate caller SID against allowed users

### 2. User Agent (WinUI 3 Application)
- **Path:** `BlueToothAutoConnect.UserAgent/`
- **Status:** 🟢 Core implementation complete
- **Implemented:**
  - [x] `DeviceWatcherService` — AEP BLE + Classic watcher, Added/Updated/Removed events
  - [x] Policy evaluation on device discovery → auto-connect if whitelisted
  - [x] `IpcClient` — named pipe client for Connect/Unpair/Ping
  - [x] `MainViewModel` — device list, whitelist CRUD, activity log, service status
  - [x] `MainPage` UI — discovered devices list, whitelist panel, log view
  - [x] Trust button → adds device to whitelist
  - [x] Connect button → sends IPC connect request
  - [x] Remove button → confirmation dialog → removes from whitelist
  - [x] DI container wired in `App.xaml.cs`
  - [ ] Auto-start at login (Task Scheduler registration)
  - [ ] System tray / minimize to tray

### 3. Policy Engine (Shared Library)
- **Path:** `BlueToothAutoConnect.PolicyEngine/`
- **Status:** 🟢 Complete
- **Implemented:**
  - [x] `IPolicyEngine` / `BluetoothPolicyEngine`
  - [x] Whitelist check, connected check, connectable check
  - [x] Replace-old-device detection
  - [x] Retry policy (default: 3 retries, 5s initial, 2x backoff, 1min max)
  - [x] IPC contracts (`IpcRequest`, `IpcResponse`, enums)
  - [x] `DeviceInfo` model
  - [ ] Concurrency limits

### 4. Persistence Layer
- **Path:** `BlueToothAutoConnect.Persistence/`
- **Status:** 🟢 Complete
- **Implemented:**
  - [x] `BluetoothDbContext` (EF Core + SQLite)
  - [x] `WhitelistEntry` entity + `IWhitelistRepository` / `WhitelistRepository`
  - [x] `DeviceHistory` entity + `IDeviceHistoryRepository` / `DeviceHistoryRepository`
  - [x] `ServiceCollectionExtensions.AddPersistenceServices`

---

## Build Status
- `BlueToothAutoConnect.PrivilegedHost` — ✅ Builds (net8.0-windows10.0.22621.0)
- `BlueToothAutoConnect.UserAgent` — ✅ Builds (net8.0-windows10.0.26100.0, x64)
- `BlueToothAutoConnect.PolicyEngine` — ✅ Builds
- `BlueToothAutoConnect.Persistence` — ✅ Builds

---

## Remaining Work

### Phase 6: Integration & Testing
- [ ] Run PrivilegedHost as Windows Service (`sc create`) and test IPC from UserAgent
- [ ] Test DeviceWatcher discovers real Bluetooth devices
- [ ] Test connect/replace-old sequence end-to-end
- [ ] Add retry loop in UserAgent IpcClient (reconnect on pipe failure)

### Phase 7: Deployment & Hardening
- [ ] Register UserAgent auto-start via Task Scheduler at login
- [ ] Add system tray icon (minimize to tray, not close)
- [ ] Validate caller SID in IpcServer (restrict to current user)
- [ ] Create MSI/MSIX installer
- [ ] Add unit tests for PolicyEngine and Persistence
- [ ] Code signing

---

## Technical Decisions

| Area | Decision | Rationale |
|------|----------|-----------|
| Storage | SQLite + EF Core | Query support, migrations |
| IPC | Named Pipes + Windows ACL | Built-in Windows auth |
| UI | WinUI 3 | Modern Windows UI |
| Service | .NET Worker Service | DI + hosting support |
| DI in UserAgent | `Microsoft.Extensions.DependencyInjection` | Consistent with service |

---

## Known Issues
- `WMC1509` warning during UserAgent build (no LocalAssembly in XAML pass 2) — cosmetic only, build succeeds
- Converters loaded via `ms-appx:///` ResourceDictionary merge to work around WinUI single-project XAML compiler limitation
