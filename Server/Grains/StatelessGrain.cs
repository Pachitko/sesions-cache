using Core;
using Grains.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Orleans.Runtime;
using Orleans.Streams;

namespace Server.Grains;

[ImplicitStreamSubscription(Constants.SessionStreamNamespace)]
public sealed class StatelessGrain(
        IClusterClient clusterClient,
        IMemoryCache memoryCache)
    : Grain, IStatelessGrain
{
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"OnActivateAsync {this.GetPrimaryKey()}");
        
        var streamProvider = this.GetStreamProvider(Constants.StreamProvider);

        var streamId = StreamId.Create(Constants.SessionStreamNamespace, this.GetPrimaryKey());
        var stream = streamProvider.GetStream<int>(streamId); 
      
        await stream
            .SubscribeAsync(data =>
            {
                Console.WriteLine(string.Join('|', data.Select(x => x.Item)));
                return Task.CompletedTask;
            });
    }

    public Task Add(string key)
    {
        var entry = memoryCache.Get<int?>(key);

        if (entry is null)
        {
            memoryCache.Set(key, 0, TimeSpan.FromSeconds(30));
        }
        else
        {
            memoryCache.Set(key, entry + 1, TimeSpan.FromSeconds(30));
        }

        return Task.CompletedTask;
    }

    public Task<int?> Get(string key)
    {
        var entry = memoryCache.Get<int?>(key);
        
        return Task.FromResult(entry);
    }
}