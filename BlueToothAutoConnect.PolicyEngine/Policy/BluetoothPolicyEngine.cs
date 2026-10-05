using BlueToothAutoConnect.PolicyEngine.Models;
using BlueToothAutoConnect.Persistence.Entities;
using BlueToothAutoConnect.Persistence.Repositories;

namespace BlueToothAutoConnect.PolicyEngine.Policy;

public class BluetoothPolicyEngine : IPolicyEngine
{
    private readonly IWhitelistRepository _whitelistRepository;
    private readonly IDeviceHistoryRepository _deviceHistoryRepository;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, RetryPolicy> _retryPolicies = new();

    public BluetoothPolicyEngine(
        IWhitelistRepository whitelistRepository,
        IDeviceHistoryRepository deviceHistoryRepository)
    {
        _whitelistRepository = whitelistRepository;
        _deviceHistoryRepository = deviceHistoryRepository;
    }

    public async Task<PolicyDecision> EvaluateConnectionPolicyAsync(DeviceInfo device)
    {
        var decision = new PolicyDecision();

        // Check if device is already connected
        if (device.IsConnected)
        {
            decision.ShouldConnect = false;
            decision.Reason = "Device is already connected";
            return decision;
        }

        // Check retry policy backoff
        var retryKey = !string.IsNullOrEmpty(device.Address) ? device.Address : (!string.IsNullOrEmpty(device.Name) ? device.Name : device.Id);
        var retryPolicy = GetRetryPolicy(retryKey);
        if (!retryPolicy.CanAttempt())
        {
            decision.ShouldConnect = false;
            var remaining = retryPolicy.NextAllowedAttemptUtc.HasValue 
                ? (retryPolicy.NextAllowedAttemptUtc.Value - DateTime.UtcNow).TotalSeconds 
                : 0;
            decision.Reason = $"Retry backoff active for device (cooldown {Math.Max(1, (int)remaining)}s remaining)";
            return decision;
        }

        // Check if device is whitelisted (by Address, Name, and/or Category rule)
        var whitelistEntry = await _whitelistRepository.FindMatchingEntryAsync(
            device.Address, device.Name, device.Category);

        if (whitelistEntry == null || !whitelistEntry.IsActive)
        {
            decision.ShouldConnect = false;
            decision.Reason = "Device is not whitelisted or whitelist entry is inactive";
            decision.IsWhitelisted = false;
            return decision;
        }

        decision.IsWhitelisted = true;

        // Check if device is connectable or already paired
        if (!device.IsConnectable && !device.IsPaired)
        {
            decision.ShouldConnect = false;
            decision.Reason = "Device is not connectable and not yet paired";
            decision.RequiresConfirmation = true;
            return decision;
        }

        // All checks passed - device should connect
        decision.ShouldConnect = true;
        decision.Reason = device.IsPaired 
            ? "Device is whitelisted and paired" 
            : "Device is whitelisted and connectable";
        return decision;
    }

    public async Task<bool> ShouldReplaceOldDeviceAsync(string deviceAddress, string newDeviceId)
    {
        // Get existing devices with the same address
        var existingDevices = await _deviceHistoryRepository.GetByDeviceAddressAsync(deviceAddress);

        // If there are existing devices with the same address but different DeviceId,
        // this indicates a replacement scenario
        var shouldReplace = existingDevices.Any(d => d.DeviceId != newDeviceId && d.ReplacedByDeviceId == null);

        return shouldReplace;
    }

    public RetryPolicy GetRetryPolicy(string deviceKey)
    {
        return _retryPolicies.GetOrAdd(deviceKey, _ => new RetryPolicy());
    }
}