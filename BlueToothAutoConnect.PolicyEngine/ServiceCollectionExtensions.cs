using BlueToothAutoConnect.PolicyEngine.Policy;
using Microsoft.Extensions.DependencyInjection;

namespace BlueToothAutoConnect.PolicyEngine;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPolicyEngineServices(
        this IServiceCollection services)
    {
        services.AddScoped<IPolicyEngine, Policy.BluetoothPolicyEngine>();

        return services;
    }
}