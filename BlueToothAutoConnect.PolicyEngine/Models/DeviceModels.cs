namespace BlueToothAutoConnect.PolicyEngine.Models;

/// <summary>
/// Represents a Bluetooth device discovered by the system
/// </summary>
public class DeviceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty; // MAC address
    public string Category { get; set; } = string.Empty;
    public bool IsPaired { get; set; }
    public bool IsConnected { get; set; }
    public bool IsConnectable { get; set; }
    public DateTime LastSeen { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
}

public static class DeviceCategoryClassifier
{
    public static string Infer(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Other";
        var lower = name.ToLowerInvariant();
        if (lower.Contains("mouse")) return "Input.Mouse";
        if (lower.Contains("keyboard") || lower.Contains("keypad")) return "Input.Keyboard";
        if (lower.Contains("buds") || lower.Contains("headset") || lower.Contains("headphones") || lower.Contains("audio") || lower.Contains("sound") || lower.Contains("speaker") || lower.Contains("fort"))
            return "Communication.Headset.Bluetooth";
        if (lower.Contains("phone") || lower.Contains("moto") || lower.Contains("galaxy") || lower.Contains("iphone"))
            return "Communication.Phone";
        return "Bluetooth Device";
    }
}

