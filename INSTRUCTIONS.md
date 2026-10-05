## High-level architecture (service + user agent)

- 1. Privileged Host (Windows Service, runs at boot, LocalSystem)

- o Owns elevated operations: programmatic pair/unpair, adapter resets, persistent privileged state.

- o Exposes a secure IPC API (named pipes or Windows RPC) for the user agent to request privileged actions.

- o Starts automatically at boot.

- 2. User Agent (per-user background app, auto-start at login)

- o Runs in the interactive session to access DeviceWatcher and show UI.

- o Discovers devices, shows device list, lets user mark devices trusted (whitelist).

- o Sends connect/unpair requests to the Privileged Host when user-approved or policy- driven.

- 3. Policy Engine (shared library)

- o Implements whitelist rules, replace-old-on-new-appearance, retry/backoff, concurrency limits.

- o Shared by both components (NuGet/local DLL) so logic is identical.

- 4. Persistence

- o SQLite (or JSON for MVP) for whitelist and device history.

- o Use DPAPI for any secrets; store service config in ProgramData with ACLs.

- 5. IPC & Security

- o Named pipes with Windows authentication; restrict to specific user SIDs or use mutual auth tokens.

- o Validate requests server-side; log actions and require UI confirmation for destructive ops.

- 6. Installer / Updater

- o MSIX or MSI that installs the service (requires admin) and registers the user-agent to auto-start.

- o Code-signed binaries; service account LocalSystem or a dedicated service account with required rights.

## Component responsibilities & flow

## Device discovery and decision

- User Agent: runs DeviceWatcher (Windows.Devices.Enumeration.DeviceWatcher) to list paired, connected, and discovered devices and raises Added/Updated/Removed events.


- Policy Engine: on Added or Updated, checks whitelist and last-known addresses. If device is trusted and not connected, request connect via IPC.

## Connect / Replace-old sequence

- 1. User Agent detects trusted device discovered (not connected).

- 2. User Agent requests Privileged Host: ConnectToDevice(deviceId, profileHint).

- 3. Privileged Host performs pairing/connect using DeviceInformation.Pairing.PairAsync or appropriate profile APIs (BLE: BluetoothLEDevice; Classic: RfcommDeviceService).

- 4. On successful connection, Privileged Host searches persisted device entries for the same Bluetooth address (MAC) and calls UnpairAsync on the old DeviceInformation entry to remove it.

- 5. Privileged Host returns success; User Agent updates UI and history.

## Handling same-address new device

- Match by Bluetooth address (not display name). When a new DeviceInformation appears with an address already in history, treat it as replacement: connect to new, then unpair old.

## Key Windows APIs & implementation notes

- Discovery: DeviceWatcher via Windows.Devices.Enumeration.

- BLE device object: Windows.Devices.Bluetooth.BluetoothLEDevice.

- Classic / RFCOMM: Windows.Devices.Bluetooth.Rfcomm.RfcommDeviceService.

- Pairing / Unpairing: DeviceInformation.Pairing.PairAsync() and DeviceInformation.Pairing.UnpairAsync(); check DeviceInformation.Id and Properties["System.Devices.Aep.DeviceAddress"] for address.

- Permissions: pairing/unpairing may require elevation or user consent; run those calls inside the Privileged Host.

- Background constraints: Windows Services run in session 0 and cannot show UI; keep UI in User Agent. Use IPC for privileged ops.

- Stability: wait for connection stability (e.g., 5210s of connected state) before removing old pairing.

## Elevation, boot, and reliability

- Service at boot: install Privileged Host as a Windows Service (Automatic start). Service runs elevated always.

- User Agent auto-start: register per-user startup (Task Scheduler or Startup folder) to run at login and connect to service.

- Authentication: use Windows ACLs on named pipe; validate caller token to prevent unauthorized control.

- Failure handling: service should persist pending operations and retry after reboot; user agent should reconnect to service if IPC lost.


## Data model & storage

- Whitelist table: id, device_address, friendly_name, added_by_user, added_at, profile_flags.

- Device history: device_address, device_id, last_seen, last_connected_at, replaced_by_id.

- Storage: SQLite with EF Core for queries; JSON acceptable for MVP.

## MVP roadmap (concrete steps)

- 1. Prototype (C# console + service stub): DeviceWatcher in user process; simple JSON whitelist; named-pipe IPC stub.

- 2. Privileged Host: implement PairAsync/UnpairAsync endpoints; test unpairing old entries.

- 3. UI: small WinUI app to list devices and mark trusted; show logs.

- 4. Replace-old logic: implement address matching and safe removal after connection stability.

- 5. Installer: create MSI/MSIX that installs service and registers user-agent startup; sign binaries.

- 6. Hardening: secure IPC, add logging, add retry/backoff, add unit/integration tests.

## Security & UX cautions

- Always require explicit user consent in UI before removing pairings.

- Unpairing can break other apps; log and allow undo for a short window.

- Keep privileged surface minimal; validate every IPC request.

## Versioning & Tracking Requirements

- **Version Bump on Every Change**: Every time a new version or any changes (major, minor, patch, or any fix) are introduced, the application version MUST be bumped.
- **`VERSION` File**: A dedicated `VERSION` text file must be maintained in the application/repository root containing the current application version.
- **Version Display in UI & CLI**: The version must be clearly displayed and accessible in both the user interface (WinUI) and the CLI application (e.g., in banner/header/status/`--version`) so the active version is immediately visible to the user.
