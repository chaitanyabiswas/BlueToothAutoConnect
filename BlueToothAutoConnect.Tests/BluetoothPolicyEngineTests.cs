using BlueToothAutoConnect.Persistence.Entities;
using BlueToothAutoConnect.Persistence.Repositories;
using BlueToothAutoConnect.PolicyEngine.Models;
using BlueToothAutoConnect.PolicyEngine.Policy;
using Moq;
using Xunit;

namespace BlueToothAutoConnect.Tests;

public class BluetoothPolicyEngineTests
{
    private readonly Mock<IWhitelistRepository> _mockWhitelistRepo;
    private readonly Mock<IDeviceHistoryRepository> _mockHistoryRepo;
    private readonly BluetoothPolicyEngine _policyEngine;

    public BluetoothPolicyEngineTests()
    {
        _mockWhitelistRepo = new Mock<IWhitelistRepository>();
        _mockHistoryRepo = new Mock<IDeviceHistoryRepository>();
        _policyEngine = new BluetoothPolicyEngine(_mockWhitelistRepo.Object, _mockHistoryRepo.Object);
    }

    [Fact]
    public async Task EvaluateConnectionPolicy_AlreadyConnected_ShouldNotConnect()
    {
        var device = new DeviceInfo
        {
            Address = "AA:BB:CC:DD:EE:FF",
            IsConnected = true,
            IsConnectable = true
        };

        var decision = await _policyEngine.EvaluateConnectionPolicyAsync(device);

        Assert.False(decision.ShouldConnect);
        Assert.Equal("Device is already connected", decision.Reason);
    }

    [Fact]
    public async Task EvaluateConnectionPolicy_NotWhitelisted_ShouldNotConnect()
    {
        var device = new DeviceInfo
        {
            Address = "AA:BB:CC:DD:EE:FF",
            Name = "Test Device",
            Category = "Input.Mouse",
            IsConnected = false,
            IsConnectable = true
        };

        _mockWhitelistRepo
            .Setup(r => r.FindMatchingEntryAsync(device.Address, device.Name, device.Category))
            .ReturnsAsync((WhitelistEntry?)null);

        var decision = await _policyEngine.EvaluateConnectionPolicyAsync(device);

        Assert.False(decision.ShouldConnect);
        Assert.False(decision.IsWhitelisted);
        Assert.Equal("Device is not whitelisted or whitelist entry is inactive", decision.Reason);
    }

    [Fact]
    public async Task EvaluateConnectionPolicy_WhitelistedAndConnectable_ShouldConnect()
    {
        var device = new DeviceInfo
        {
            Address = "AA:BB:CC:DD:EE:FF",
            Name = "Test Mouse",
            Category = "Input.Mouse",
            IsConnected = false,
            IsConnectable = true
        };

        _mockWhitelistRepo
            .Setup(r => r.FindMatchingEntryAsync(device.Address, device.Name, device.Category))
            .ReturnsAsync(new WhitelistEntry
            {
                DeviceAddress = device.Address,
                DeviceName = device.Name,
                Category = device.Category,
                IsActive = true
            });

        var decision = await _policyEngine.EvaluateConnectionPolicyAsync(device);

        Assert.True(decision.ShouldConnect);
        Assert.True(decision.IsWhitelisted);
        Assert.Equal("Device is whitelisted and connectable", decision.Reason);
    }

    [Fact]
    public async Task EvaluateConnectionPolicy_NameOnlyWhitelist_ShouldConnect()
    {
        var device = new DeviceInfo
        {
            Address = "11:22:33:44:55:66",
            Name = "BT5.2 Mouse",
            Category = "Input.Mouse",
            IsConnected = false,
            IsConnectable = true
        };

        _mockWhitelistRepo
            .Setup(r => r.FindMatchingEntryAsync(device.Address, device.Name, device.Category))
            .ReturnsAsync(new WhitelistEntry
            {
                DeviceName = "BT5.2 Mouse",
                IsActive = true
            });

        var decision = await _policyEngine.EvaluateConnectionPolicyAsync(device);

        Assert.True(decision.ShouldConnect);
        Assert.True(decision.IsWhitelisted);
    }

    [Fact]
    public async Task EvaluateConnectionPolicy_CategoryOnlyWhitelist_ShouldConnect()
    {
        var device = new DeviceInfo
        {
            Address = "99:88:77:66:55:44",
            Name = "Unknown Headset",
            Category = "Communication.Headset.Bluetooth",
            IsConnected = false,
            IsConnectable = true
        };

        _mockWhitelistRepo
            .Setup(r => r.FindMatchingEntryAsync(device.Address, device.Name, device.Category))
            .ReturnsAsync(new WhitelistEntry
            {
                Category = "Communication.Headset.Bluetooth",
                IsActive = true
            });

        var decision = await _policyEngine.EvaluateConnectionPolicyAsync(device);

        Assert.True(decision.ShouldConnect);
        Assert.True(decision.IsWhitelisted);
    }

    [Fact]
    public async Task ShouldReplaceOldDevice_DifferentDeviceIdSameAddress_ReturnsTrue()
    {
        const string address = "AA:BB:CC:DD:EE:FF";
        const string newDeviceId = "Bluetooth#Bluetooth00:11:22:33:44:55-new";

        _mockHistoryRepo
            .Setup(r => r.GetByDeviceAddressAsync(address))
            .ReturnsAsync(new List<DeviceHistory>
            {
                new DeviceHistory
                {
                    DeviceAddress = address,
                    DeviceId = "Bluetooth#Bluetooth00:11:22:33:44:55-old",
                    ReplacedByDeviceId = null
                }
            });

        var shouldReplace = await _policyEngine.ShouldReplaceOldDeviceAsync(address, newDeviceId);

        Assert.True(shouldReplace);
    }

    [Fact]
    public void GetRetryPolicy_CreatesAndReturnsPolicy()
    {
        const string address = "AA:BB:CC:DD:EE:FF";
        var policy = _policyEngine.GetRetryPolicy(address);

        Assert.NotNull(policy);
        Assert.Equal(3, policy.MaxRetries);
        Assert.Same(policy, _policyEngine.GetRetryPolicy(address));
    }

    [Fact]
    public void RetryPolicy_RecordAttempt_CalculatesExponentialBackoff()
    {
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(2),
            BackoffMultiplier = 2.0,
            MaxDelay = TimeSpan.FromSeconds(10)
        };

        Assert.True(policy.CanAttempt());

        // Attempt 1: 2s
        var delay1 = policy.RecordAttempt(false);
        Assert.Equal(2.0, delay1.TotalSeconds);
        Assert.False(policy.CanAttempt(DateTime.UtcNow));

        // Attempt 2: 4s
        var delay2 = policy.RecordAttempt(false);
        Assert.Equal(4.0, delay2.TotalSeconds);

        // Attempt 3: 8s
        var delay3 = policy.RecordAttempt(false);
        Assert.Equal(8.0, delay3.TotalSeconds);

        // Attempt 4: capped at 10s
        var delay4 = policy.RecordAttempt(false);
        Assert.Equal(10.0, delay4.TotalSeconds);

        // Success resets
        policy.RecordAttempt(true);
        Assert.Equal(0, policy.CurrentAttemptCount);
        Assert.True(policy.CanAttempt());
    }

    [Fact]
    public async Task EvaluateConnectionPolicyAsync_RetryBackoffActive_ReturnsShouldConnectFalse()
    {
        const string address = "11:22:33:44:55:66";
        var device = new DeviceInfo
        {
            Id = "device-1",
            Address = address,
            Name = "Whitelisted Device",
            IsConnected = false,
            IsConnectable = true
        };

        _mockWhitelistRepo
            .Setup(r => r.FindMatchingEntryAsync(address, device.Name, device.Category))
            .ReturnsAsync(new WhitelistEntry
            {
                DeviceAddress = address,
                FriendlyName = "Whitelisted Device",
                IsActive = true
            });

        // Trigger failed attempt to engage cooldown
        var policy = _policyEngine.GetRetryPolicy(address);
        policy.RecordAttempt(false);

        var decision = await _policyEngine.EvaluateConnectionPolicyAsync(device);

        Assert.False(decision.ShouldConnect);
        Assert.Contains("Retry backoff active", decision.Reason);
    }
}
