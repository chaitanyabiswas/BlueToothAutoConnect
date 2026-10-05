namespace BlueToothAutoConnect.PolicyEngine.IPC;

/// <summary>
/// IPC request types for communication between User Agent and Privileged Host
/// </summary>
public enum IpcRequestType
{
    ConnectToDevice,
    UnpairDevice,
    GetDeviceStatus,
    ResetBluetoothAdapter,
    Ping,
    FinalizeConnection
}

/// <summary>
/// Base IPC request message
/// </summary>
public class IpcRequest
{
    public IpcRequestType RequestType { get; set; }
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public string UserSid { get; set; } = string.Empty; // For authentication
}

/// <summary>
/// IPC response status
/// </summary>
public enum IpcResponseStatus
{
    Success,
    Error,
    Unauthorized,
    NotFound,
    InvalidParameters
}

/// <summary>
/// Base IPC response message
/// </summary>
public class IpcResponse
{
    public string RequestId { get; set; } = string.Empty;
    public IpcResponseStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Data { get; set; } = new();
    public List<string> PendingUnpairDeviceIds { get; set; } = new();
}

/// <summary>
/// Specific request for connecting to a device
/// </summary>
public class ConnectDeviceRequest : IpcRequest
{
    public ConnectDeviceRequest()
    {
        RequestType = IpcRequestType.ConnectToDevice;
    }

    public string DeviceId { get; set; } = string.Empty;
    public string? ProfileHint { get; set; } // e.g., "BLE" or "Classic"
}

/// <summary>
/// Specific request for unpairing a device
/// </summary>
public class UnpairDeviceRequest : IpcRequest
{
    public UnpairDeviceRequest()
    {
        RequestType = IpcRequestType.UnpairDevice;
    }

    public string DeviceId { get; set; } = string.Empty;
    public bool Confirm { get; set; } // Require explicit confirmation
}

/// <summary>
/// Specific request for getting device status
/// </summary>
public class GetDeviceStatusRequest : IpcRequest
{
    public GetDeviceStatusRequest()
    {
        RequestType = IpcRequestType.GetDeviceStatus;
    }

    public string DeviceId { get; set; } = string.Empty;
}