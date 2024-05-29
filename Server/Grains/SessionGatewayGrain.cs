using Grains.Interfaces;
using Orleans.Runtime;

namespace Server.Grains;

public sealed class SessionGatewayGrain 
    : Grain, ISessionGatewayGrain
{
    public async Task Add(string key)
    {
        var streamProvider = this.GetStreamProvider("StreamProvider");
        
        var streamId = StreamId.Create("sessions", Guid.Parse("F0E912C4-7CAB-4868-8A8F-8950E252C387").GetHashCode());
        var stream = streamProvider.GetStream<int>(streamId);

        await stream.OnNextAsync(1);
    }

    public Task<int?> Get(string key)
    {
        throw new NotImplementedException();
    }
}