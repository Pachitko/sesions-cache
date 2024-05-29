using System.Threading.Channels;
using Core;
using Core.Interfaces;
using Grains.Exception;
using Grains.Interfaces;
using Grains.Messages;
using Grains.States;
using Orleans.Runtime;

namespace Server.Grains;

[SessionPlacementStrategy]
public sealed class SessionGrain(
        ISessionDataRepository sessionDataRepository,
        ILogger<SessionGrain> logger,
        ChannelWriter<SessionState> sessionGrainStateWriter,
        ILocalSiloDetails localSiloDetails,
        IClusterClient clusterClient,
        ILocalSessionCache localSessionCache)
        // ISiloStatusListener siloStatusListener)
        // IGatewayListProvider gatewayListProvider,
        // IClusterMembershipService clusterMembershipService,
        // IManagementGrain managementGrain) 
        : Grain, ISessionGrain
{
    private IDisposable? _replicationTimer;
    
    private SessionState _state = new()
    {
        Id = Guid.Empty,
        ExpirationUnixSeconds = 0
    };

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning("Activating: {Id} {SiloAddress}", 
            this.GetPrimaryKeyString(), localSiloDetails.SiloAddress);

        if (localSessionCache.Exists(this.GetPrimaryKey()))
        {
            logger.LogWarning("Migrate session {SessionId}", this.GetPrimaryKey());
            MigrateOnIdle();
        }
        
        //todo: get actual if exists after redeploy
        
        // if (await sessionDataRepository.Exists(this.GetPrimaryKey(), cancellationToken))
        // {
        //     throw new SessionAlreadyExistsException(this.GetPrimaryKey().ToString());
        // }
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        logger.LogWarning("Deactivating: {Id} {SiloAddress}", 
            this.GetPrimaryKeyString(), localSiloDetails.SiloAddress);
        
        localSessionCache.Remove(this.GetPrimaryKey());

        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    public async Task Update(UpdateSessionCommand command)
    {
        var primaryKey = this.GetPrimaryKey();
        
        if (command.IsEmpty())
        {
            return;
        }
        
        var utcNow = DateTimeOffset.UtcNow;
        var unixSecondsNow = utcNow.ToUnixTimeSeconds();
        
        if (command.ExpirationUnixSeconds.HasValue)
        {
            var expirationTimeSpan = DateTimeOffset.FromUnixTimeSeconds(command.ExpirationUnixSeconds.Value) - utcNow;
            
            localSessionCache.Add(this.GetPrimaryKey(), expirationTimeSpan);

            logger.LogWarning(expirationTimeSpan.ToString());
            DelayDeactivation(expirationTimeSpan);
        }
        
        if (_state.IsEmpty())
        {
            if (!command.ExpirationUnixSeconds.HasValue)
            {
                throw new ArgumentNullException(nameof(command.ExpirationUnixSeconds));
            }
            
            _state = new SessionState
            {
                Id = primaryKey,
                ExpirationUnixSeconds = unixSecondsNow + command.ExpirationUnixSeconds.Value,
                Data = command.Sections
                    .ToDictionary(
                        x => x.Key, 
                        x => new SessionState.SectionData 
                        {
                            Data = x.Value
                        })
            };
        }
        else
        {
            if (command.ExpirationUnixSeconds.HasValue)
            {
                _state.ExpirationUnixSeconds = unixSecondsNow + command.ExpirationUnixSeconds.Value;
            }

            var concurrencyUpdateSections = UpdateSections(command.Sections).ToArray();
            
            if (concurrencyUpdateSections.Length != 0)
            {
                throw new ConcurrencyException
                {
                    SectionIds = concurrencyUpdateSections
                };
            }
        }
        
        if (_state.ExpirationUnixSeconds <= unixSecondsNow)
        { 
            throw new SessionExpiredException(this.GetPrimaryKey().ToString());
        }

        await sessionGrainStateWriter.WriteAsync(_state);

        if (RequestContext.Get(Constants.SessionGrainOrder) is int sessionGrainOrder and 1)
        {
            await ReplicateAsync(command, sessionGrainOrder);
        }
    }
    
    public async Task<SessionState> Get()
    {
        logger.LogWarning("Get {Key}", this.GetPrimaryKeyString());

        if (_state.IsEmpty())
        {
            if (!await sessionDataRepository.Exists(this.GetPrimaryKey(), CancellationToken.None))
            {
                throw new SessionNotFoundException(this.GetPrimaryKey().ToString());
            }
            
            if (RequestContext.Get(Constants.SessionGrainOrder) is int sessionGrainOrder and 1)
            {
                var replica = clusterClient.GetGrain<ISessionGrain>(this.GetPrimaryKey(), sessionGrainOrder.ToString());
                RequestContext.Set(Constants.SessionGrainOrder, sessionGrainOrder + 1);
                return await replica.Get();
            }
            
            throw new SessionNotFoundException(this.GetPrimaryKey().ToString());
        }

        if (_state.ExpirationUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            throw new SessionExpiredException(this.GetPrimaryKey().ToString());
        
        return _state;
    }

    public async ValueTask Invalidate()
    {
        logger.LogWarning("Invalidate {Key}", this.GetPrimaryKeyString());

        DeactivateOnIdle();
        localSessionCache.Remove(this.GetPrimaryKey());
        
        if (_state.IsEmpty() &&
            !await sessionDataRepository.Exists(this.GetPrimaryKey(), CancellationToken.None))
        {
            throw new SessionNotFoundException(this.GetPrimaryKey().ToString());
        }

        if (RequestContext.Get(Constants.SessionGrainOrder) is int sessionGrainOrder and 1)
        {
            var replica = clusterClient.GetGrain<ISessionGrain>(this.GetPrimaryKey(), sessionGrainOrder.ToString());
            RequestContext.Set(Constants.SessionGrainOrder, sessionGrainOrder + 1);
            await replica.Invalidate();
        }
        
        await sessionDataRepository.Delete(this.GetPrimaryKey(), CancellationToken.None);
        
        _state.ExpirationUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
    
    private async Task ReplicateAsync(UpdateSessionCommand command, int sessionGrainOrder)
    {
        var replicaOrder = sessionGrainOrder + 1;
        var replica = clusterClient.GetGrain<ISessionGrain>(this.GetPrimaryKey(), replicaOrder.ToString());
        RequestContext.Set(Constants.SessionGrainOrder, replicaOrder);
        try
        {
            await replica.Update(command);
            _replicationTimer?.Dispose();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Retry replication for session {SessionId} {Order}", this.GetPrimaryKey(), replicaOrder);

            if (_replicationTimer is not null)
            {
                return;
            }
            
            _replicationTimer = RegisterTimer(
                asyncCallback: commandState => ReplicateAsync((UpdateSessionCommand)commandState, sessionGrainOrder),
                state: command,
                dueTime:TimeSpan.FromSeconds(3),
                period: TimeSpan.FromSeconds(15));
        }
    }
    
    private IEnumerable<string> UpdateSections(IEnumerable<SectionData> sections)
    {
        foreach (var section in sections)
        {
            if (_state.Data.TryGetValue(section.Key, out var sectionData))
            {
                if (section.Version != sectionData.Version)
                {
                    yield return section.Key;
                }
                else
                {
                    sectionData.Data = section.Value;
                }
            }
            else
            {
                _state.Data[section.Key] = new SessionState.SectionData
                {
                    Data = section.Value
                };
            }
        }
    }
}