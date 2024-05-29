using Grains.Interfaces;
using Grains.States;
using Orleans.EventSourcing;
using Orleans.Providers;

namespace Server.Grains;

[StorageProvider(ProviderName = "StateStorage")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
public sealed class ReplicatedGrain : JournaledGrain<ReplicatedGrainState>, IReplicatedGrain
{
    public Task Add()
    {
        RaiseEvent(new AddEvent());

        return ConfirmEvents();
    }

    public Task<int> Get()
    {
        return Task.FromResult(State.Value);
    }
}