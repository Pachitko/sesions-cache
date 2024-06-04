using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Server.Options;

namespace Server.BackgroundServices;

internal sealed class OpaBackgroundService(
        IMemoryCache memoryCache,
        IOptions<ServerOptions> serverOptions, 
        IHttpClientFactory httpClientFactory) 
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(serverOptions.Value.OpenPolicyAgentHost);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            var policyJson = await client.GetAsync("/v1/policies/services_rbac", stoppingToken);

            memoryCache.Set("opa_policy", policyJson);

            await Task.Delay(serverOptions.Value.OpenPolicyUpdateDelay, stoppingToken);
        }
    }
}