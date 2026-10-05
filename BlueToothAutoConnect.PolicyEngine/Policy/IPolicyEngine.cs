using BlueToothAutoConnect.PolicyEngine.Models;

namespace BlueToothAutoConnect.PolicyEngine.Policy;

public interface IPolicyEngine
{
    /// <summary>
    /// Evaluates whether a device should be connected based on policy rules
    /// </summary>
    Task<PolicyDecision> EvaluateConnectionPolicyAsync(DeviceInfo device);

    /// <summary>
    /// Checks if a device with the same address should replace an existing device
    /// </summary>
    Task<bool> ShouldReplaceOldDeviceAsync(string deviceAddress, string newDeviceId);

    /// <summary>
    /// Gets the retry configuration for a device
    /// </summary>
    RetryPolicy GetRetryPolicy(string deviceAddress);
}

/// <summary>
/// Represents the decision made by the policy engine
/// </summary>
public class PolicyDecision
{
    public bool ShouldConnect { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool IsWhitelisted { get; set; }
    public bool RequiresConfirmation { get; set; }
}

/// <summary>
/// Retry policy configuration
/// </summary>
public class RetryPolicy
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(1);
    public double BackoffMultiplier { get; set; } = 2.0;

    public int CurrentAttemptCount { get; private set; }
    public DateTime? NextAllowedAttemptUtc { get; private set; }

    public bool CanAttempt(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        if (NextAllowedAttemptUtc.HasValue && now < NextAllowedAttemptUtc.Value)
        {
            return false;
        }
        return true;
    }

    public TimeSpan RecordAttempt(bool success, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        if (success)
        {
            Reset();
            return TimeSpan.Zero;
        }

        CurrentAttemptCount++;
        double delaySeconds = InitialDelay.TotalSeconds * Math.Pow(BackoffMultiplier, Math.Max(0, CurrentAttemptCount - 1));
        var delay = TimeSpan.FromSeconds(delaySeconds);
        if (delay > MaxDelay)
        {
            delay = MaxDelay;
        }

        NextAllowedAttemptUtc = now + delay;
        return delay;
    }

    public void Reset()
    {
        CurrentAttemptCount = 0;
        NextAllowedAttemptUtc = null;
    }
}