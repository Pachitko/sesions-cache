using Core;
using Core.Models;
using Grains.Exception;
using Grains.GrainExtensions;
using Grains.Interfaces;
using Grains.Messages;
using Grains.States;
using Orleans.Runtime;
using Server.Options;

namespace Server.Grains;

[SessionPlacementStrategy]
public sealed class SessionGrain(GrainSingletonService services) : Grain, ISessionGrain
{
    private readonly ServerOptions _options = services.Options.Value;
    
    private SiloAddress? _next;
    
    private IDisposable? _replicationTimer;

    private int _order;

    private SessionState _state = SessionState.Empty();

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        SetNext((SiloAddress?)RequestContext.Get(Constants.Next));
        _order = (int)RequestContext.Get(Constants.SessionGrainOrder);
        
        services.LocalSessionCache.Add(this.GetPrimaryKey(), TimeSpan.FromHours(1));
        
        if (services.Logger.IsEnabled(LogLevel.Warning))
            services.Logger.LogWarning("Activating: {Id} {SiloAddress}", 
                this.GetPrimaryKeyString(), services.LocalSiloDetails.SiloAddress);

        return Task.CompletedTask;
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        if (services.Logger.IsEnabled(LogLevel.Warning)) 
            services.Logger.LogWarning("Deactivating: {Id} {SiloAddress}",
            this.GetPrimaryKeyString(), services.LocalSiloDetails.SiloAddress);
        
        services.LocalSessionCache.Remove(this.GetPrimaryKey());

        if (IsExpired())
        {
            await services.SessionDeletionWriter.WriteAsync(new SessionDeletion(this.GetPrimaryKey(), "expired"), cancellationToken);
        }
        else
        {
            await Replicate(nameof(OnDeactivateAsync));
        }
    }

    public async Task Update(UpdateSessionCommand command)
    {
        if (services.Logger.IsEnabled(LogLevel.Warning)) 
            services.Logger.LogWarning("{SessionId} {@CommandSections}", 
                this.GetPrimaryKeyString(), command.Sections.Select(x => x.Key));
        
        var primaryKey = this.GetPrimaryKey();
        
        if (command.ExpirationUnixSeconds.HasValue) 
        {
            var expirationTimeSpan = 
                DateTimeOffset.FromUnixTimeSeconds(command.ExpirationUnixSeconds.Value) - DateTimeOffset.UtcNow;

            DelayDeactivation(expirationTimeSpan);
        }
        
        if (_state.IsEmpty())
        {
            if (_order == 1)
            {
                await services.SessionGrainStateWriter.WriteAsync(_state);
            }

            services.PermissionService.CheckAccess(command.ServiceId, command.Sections.Select(x => (Section: x.Key, action: 'c')));
            
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
                    .Select(x => new SectionData 
                    {
                        Name = x.Key,
                        Version = 1,
                        Data = x.Value
                    })
                    .ToList()
            };
        }
        else
        {
            if (command.ExpirationUnixSeconds.HasValue)
            {
                _state.ExpirationUnixSeconds = command.ExpirationUnixSeconds.Value;
                _state.Version++;
            }

            var concurrencyUpdateSections = UpdateSections(command.ServiceId, command.Sections).ToArray();
            
            if (concurrencyUpdateSections.Length != 0)
            {
                throw new ConcurrencyException
                {
                    SectionIds = concurrencyUpdateSections
                };
            }
        }

        CheckExpiration();
        
        if (_order == 1 && RequestContext.Get(Constants.Next) is null)
        {
            await Replicate(nameof(Update), command);
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
        if (services.Logger.IsEnabled(LogLevel.Warning))
            services.Logger.LogWarning("Get {Key}", this.GetPrimaryKeyString());

        if (_state.IsEmpty())
        {
            if (services.Logger.IsEnabled(LogLevel.Warning))
                services.Logger.LogWarning("Empty state on get {Key}", this.GetPrimaryKeyString());
            // if (!await sessionDataRepository.Exists(this.GetPrimaryKey(), CancellationToken.None))
            // {
            //     return null;
            // }
            
            if (_order > 1 || !ShouldReplicate())
                return null;
            
            var replica = GrainFactory.GetGrain<ISessionGrain>(this.GetPrimaryKey(), (_order + 1).ToString());
            
            RequestContext.Set(Constants.SessionGrainOrder, _order + 1);
            var replicaData = await replica.Get(query);

            _next = await replica.AsReference<ISessionGrainExtension>().GetSiloAddress();

            if (replicaData is not null)
            {
                _state = SessionState.FromSessionData(this.GetPrimaryKey(), replicaData);
            }
                
            return replicaData;
        }

        CheckExpiration();

        services.PermissionService.CheckAccess(query.ServiceId, Constants.ReadSectionActionCode, query.Sections);
        
        if (_next is null || services.SiloStatusOracle.IsDeadSilo(_next))
        {
            await Replicate(nameof(Get));
        }

        return SessionData.FromSessionState(_state, query.Sections);
    }

    public async ValueTask<bool> Invalidate(InvalidateCommand command)
    {
        services.PermissionService.CheckAccess(command.ServiceId, Constants.InvalidateSession);

        if (services.Logger.IsEnabled(LogLevel.Warning))
            services.Logger.LogWarning("Invalidate {Key}", this.GetPrimaryKeyString());

        DeactivateOnIdle();
        services.LocalSessionCache.Remove(this.GetPrimaryKey());
        
        if (_state.IsEmpty() && !await services.SessionDataRepository.Exists(this.GetPrimaryKey(), CancellationToken.None))
        {
            return true;
        }

        _state = SessionState.Empty();

        await services.SessionDeletionWriter.WriteAsync(new SessionDeletion(this.GetPrimaryKey(), command.Reason));

        if (_order == 1 && ShouldReplicate())
        {
            var replica = services.ClusterClient.GetGrain<ISessionGrain>(this.GetPrimaryKey(), (_order + 1).ToString());
            RequestContext.Set(Constants.SessionGrainOrder, _order + 1);
            return await replica.Invalidate(command);
        }

        return true;
    }
    
    private async Task Replicate(string reason, UpdateSessionCommand? command = null, bool fromTimer = false)
    {
        if (!ShouldReplicate())
        {
            return;
        }
        
        if (!fromTimer && _options.ReplicationType == ReplicationType.Async)
        {
            StartReplicationTimer();
            return;
        }
        
        _replicationTimer?.Dispose();
        _replicationTimer = null;
        
        var replicaOrder = _order == 1 ? 2 : 1;
        
        if (services.Logger.IsEnabled(LogLevel.Warning))
            services.Logger.LogWarning(
                "{SessionId} is replicating to {ReplicaOrder}. Reason: {Reason}",
                this.GetPrimaryKeyString(), replicaOrder, reason);

        command ??= new UpdateSessionCommand
        {
            Sections = _state.Data
                .Select(x => new CreateSectionData
                {
                    Key = x.Name,
                    Value = x.Data,
                    Version = x.Version
                })
                .ToArray(),
            ExpirationUnixSeconds = _state.ExpirationUnixSeconds
        };
        
        try
        {
            var replica = services.ClusterClient.GetGrain<ISessionGrain>(this.GetPrimaryKey(), replicaOrder.ToString());
            RequestContext.Set(Constants.SessionGrainOrder, replicaOrder);
            RequestContext.Set(Constants.Next, services.LocalSiloDetails.SiloAddress);

            await replica.Update(command);
            
            var replicaAddress = await replica.AsReference<ISessionGrainExtension>().GetSiloAddress();
            
            SetNext(replicaAddress);
            
            _replicationTimer?.Dispose();
            _replicationTimer = null;
        }
        catch (Exception e)
        {
            if (services.Logger.IsEnabled(LogLevel.Warning))
                services.Logger.LogError(e, "Start replication retrying for session {SessionId} {Order}", this.GetPrimaryKey(), replicaOrder);

            if (_replicationTimer is not null)
            {
                return;
            }
            
            if (_options is { ReplicationType: ReplicationType.Sync, EnsureSynchronized: true })
            {
                throw;
            }
            
            StartReplicationTimer();
        }
    }

    private void StartReplicationTimer()
    {
        _replicationTimer = RegisterTimer(
            asyncCallback: _ => Replicate("retry", fromTimer: true),
            state: null!,
            dueTime: _options.ReplicationType == ReplicationType.Async
                ? TimeSpan.FromMilliseconds(500)
                : _options.ReplicationRetryDelay,
            period: _options.ReplicationRetryDelay);
    }
    
    private IEnumerable<string> UpdateSections(string? serviceId, IEnumerable<CreateSectionData> sections)
    {
        foreach (var section in sections)
        {
            var existingSectionData = _state.Data.FirstOrDefault(x => x.Name == section.Key);
            
            if (existingSectionData is not null)
            {
                services.PermissionService.CheckAccess(serviceId, section.Key, Constants.UpdateSectionActionCode);

                if (section.Version != existingSectionData.Version && _options.EnableConcurrencyCheckForSections)
                {
                    yield return section.Key;
                }
                else
                {
                    existingSectionData.Data = section.Value;
                    existingSectionData.Version++;
                }
            }
            else
            {
                services.PermissionService.CheckAccess(serviceId, section.Key, Constants.CreateSectionActionCode);
                
                _state.Data.Add(new SectionData
                    {
                        Name = section.Key,
                        Data = section.Value,
                        Version = 1,
                    }
                );
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

    private void SetNext(SiloAddress? next)
    {
        _next = next is null || next.Equals(services.SiloStatusOracle.SiloAddress) 
            ? null 
            : next;
    }

    private bool ShouldReplicate()
    {
        return _options.ReplicationFactor > 0;
    }
}