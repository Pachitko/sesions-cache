using System.Threading.Channels;
using Core;
using Core.Interfaces;
using Core.Models;
using Grains.Exception;
using Grains.Interfaces;
using Grains.Messages;
using Grains.States;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Server.Options;

namespace Server.Grains;

[SessionPlacementStrategy]
public sealed class SessionGrain(
        ISessionDataRepository sessionDataRepository,
        ILogger<SessionGrain> logger,
        ChannelWriter<SessionState> sessionGrainStateWriter,
        ChannelWriter<SessionDeletion> sessionDeletionWriter,
        ILocalSiloDetails localSiloDetails,
        IClusterClient clusterClient,
        ILocalSessionCache localSessionCache,
        ISiloStatusOracle siloStatusOracle,
        IOptionsSnapshot<ServerOptions> options)
        : Grain, ISessionGrain
{
    private readonly ServerOptions _options = options.Value;
    
    private SiloAddress? _next;
    
    private IDisposable? _replicationTimer;

    private int _order;

    private SessionState _state = SessionState.Empty();

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _next = (SiloAddress?)RequestContext.Get(Constants.Next);
        _order = (int)RequestContext.Get(Constants.SessionGrainOrder);

        localSessionCache.Add(this.GetPrimaryKey(), TimeSpan.FromHours(1));
        
        logger.LogWarning("Activating: {Id} {SiloAddress}", 
            this.GetPrimaryKeyString(), localSiloDetails.SiloAddress);

        return Task.CompletedTask;
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        logger.LogWarning("Deactivating: {Id} {SiloAddress}",
            this.GetPrimaryKeyString(), localSiloDetails.SiloAddress);
        
        localSessionCache.Remove(this.GetPrimaryKey());

        if (IsExpired())
        {
            await sessionDeletionWriter.WriteAsync(new SessionDeletion(this.GetPrimaryKey(), "expired"), cancellationToken);
        }
        else
        {
            await Replicate(nameof(OnDeactivateAsync));
        }
    }

    public async Task Update(UpdateSessionCommand command)
    {
        logger.LogWarning("{SessionId} {@CommandSections}", 
            this.GetPrimaryKeyString(), command.Sections.Select(x => x.Key));
        
        var primaryKey = this.GetPrimaryKey();
        
        if (command.IsEmpty())
        {
            return;
        }
        
        if (command.ExpirationUnixSeconds.HasValue) 
        {
            var expirationTimeSpan = 
                DateTimeOffset.FromUnixTimeSeconds(command.ExpirationUnixSeconds.Value) - DateTimeOffset.UtcNow;
            
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
                ExpirationUnixSeconds = command.ExpirationUnixSeconds.Value,
                Version = 1,
                Data = command.Sections
                    .ToDictionary(
                        x => x.Key, 
                        x => new SectionData 
                        {
                            Data = x.Value,
                            Version = x.Version,
                        })
            };
        }
        else
        {
            if (command.ExpirationUnixSeconds.HasValue)
            {
                _state.ExpirationUnixSeconds = command.ExpirationUnixSeconds.Value;
                _state.Version++;
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

        CheckExpiration();

        if (_order == 1)
        {
            await sessionGrainStateWriter.WriteAsync(_state);
        }

        if (_order == 1 && RequestContext.Get(Constants.Next) is null)
        {
            await Replicate(nameof(Update));
            // await sessionReplicationData.WriteAsync(new SessionReplicationData
            // {
            //     SessionId = this.GetPrimaryKey(),
            //     ReplicaOrder = 2,
            //     UpdateSessionCommand = command
            // });
        }
    }
    
    public async Task<SessionData?> Get(GetSessionDataQuery query)
    {
        logger.LogWarning("Get {Key}", this.GetPrimaryKeyString());

        if (_state.IsEmpty())
        {
            logger.LogWarning("Empty state on get {Key}", this.GetPrimaryKeyString());
            // if (!await sessionDataRepository.Exists(this.GetPrimaryKey(), CancellationToken.None))
            // {
            //     return null;
            // }
            
            if (_order > 1)
                return null;
            
            var replica = GrainFactory.GetGrain<ISessionGrain>(this.GetPrimaryKey(), (_order + 1).ToString());
            // var replicaAddress = await GrainFactory.GetGrain<IManagementGrain>(0).GetActivationAddress(replica);
            
            RequestContext.Set(Constants.SessionGrainOrder, _order + 1);
            var replicaData = await replica.Get(query);

            _next = await GrainFactory.GetGrain<IManagementGrain>(0).GetActivationAddress(replica);

            if (replicaData is not null)
            {
                _state = SessionState.FromSessionData(this.GetPrimaryKey(), replicaData);
            }
                
            return replicaData;
        }

        CheckExpiration();

        if (_next is null || siloStatusOracle.IsDeadSilo(_next) || _next.Equals(localSiloDetails.SiloAddress))
        {
            await Replicate(nameof(Get));
        }

        return SessionData.FromSessionState(_state, query.Sections);
    }

    public async ValueTask<bool> Invalidate(string reason)
    {
        logger.LogWarning("Invalidate {Key}", this.GetPrimaryKeyString());

        DeactivateOnIdle();
        localSessionCache.Remove(this.GetPrimaryKey());
        
        if (_state.IsEmpty() && !await sessionDataRepository.Exists(this.GetPrimaryKey(), CancellationToken.None))
        {
            return true;
        }

        _state = SessionState.Empty();

        if (_order == 1)
        {
            var replica = clusterClient.GetGrain<ISessionGrain>(this.GetPrimaryKey(), (_order + 1).ToString());
            RequestContext.Set(Constants.SessionGrainOrder, _order + 1);
            return await replica.Invalidate(reason);
        }
        
        await sessionDeletionWriter.WriteAsync(new SessionDeletion(this.GetPrimaryKey(), reason));

        return true;
    }
    
    private async Task Replicate(string reason)
    {
        _replicationTimer?.Dispose();
        _replicationTimer = null;
        
        var replicaOrder = _order == 1 ? 2 : 1;
        
        logger.LogWarning(
            "{SessionId} is replicating to {ReplicaOrder}. Reason: {Reason}",
            this.GetPrimaryKeyString(), replicaOrder, reason);

        var command = new UpdateSessionCommand
        {
            Sections = _state.Data
                .Select(x => new CreateSectionData
                {
                    Key = x.Key,
                    Value = x.Value.Data,
                    Version = x.Value.Version
                })
                .ToArray(),
            ExpirationUnixSeconds = _state.ExpirationUnixSeconds
        };
        
        try
        {
            var replica = clusterClient.GetGrain<ISessionGrain>(this.GetPrimaryKey(), replicaOrder.ToString());
            RequestContext.Set(Constants.SessionGrainOrder, replicaOrder);
            RequestContext.Set(Constants.Next, localSiloDetails.SiloAddress);
            
            await replica.Update(command);
            _next = await GrainFactory.GetGrain<IManagementGrain>(0).GetActivationAddress(replica);
            _replicationTimer?.Dispose();
            _replicationTimer = null;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Start replication retrying for session {SessionId} {Order}", this.GetPrimaryKey(), replicaOrder);

            if (_replicationTimer is not null)
            {
                return;
            }
            
            _replicationTimer = RegisterTimer(
                asyncCallback: _ => Replicate("retry"),
                state: null!,
                dueTime: _options.ReplicationRetryDelay,
                period: _options.ReplicationRetryDelay);
        }
    }
    
    private IEnumerable<string> UpdateSections(IEnumerable<CreateSectionData> sections)
    {
        foreach (var section in sections)
        {
            if (_state.Data.TryGetValue(section.Key, out var sectionData))
            {
                if (section.Version != sectionData.Version && _options.EnableConcurrencyCheckForSections)
                {
                    yield return section.Key;
                }
                else
                {
                    sectionData.Data = section.Value;
                    sectionData.Version = section.Version;
                }
            }
            else
            {
                _state.Data[section.Key] = new SectionData
                {
                    Data = section.Value,
                    Version = 1
                };
            }
        }
    }

    private void CheckExpiration()
    {
        if (IsExpired())
        {
            throw new SessionExpiredException(this.GetPrimaryKey().ToString());
        }
    }

    private bool IsExpired()
    {
        return _state.ExpirationUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}