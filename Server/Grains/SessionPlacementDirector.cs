using Core;
using Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Orleans.Placement;
using Orleans.Runtime;
using Orleans.Runtime.Placement;

namespace Server.Grains;

[Serializable]
[GenerateSerializer]
public sealed class SessionPlacementStrategy : PlacementStrategy;

[AttributeUsage(AttributeTargets.Class)]
public sealed class SessionPlacementStrategyAttribute() : PlacementAttribute(new SessionPlacementStrategy());

[Serializable]
public sealed class SessionPlacementDirector(
        ILogger<SessionPlacementDirector> logger,
        ILocalSessionCache localSessionCache) 
    : IPlacementDirector
{
    private static readonly object One = 1;
    
    private Task<SiloAddress>? _cachedLocalSilo;

    public Task<SiloAddress> OnAddActivation(
        PlacementStrategy strategy, PlacementTarget target, IPlacementContext context)
    { 
        logger.LogWarning("OnAddActivation {Key}", target.GrainIdentity.Key.ToString());

        if (target.RequestContextData is not null &&
            target.RequestContextData.TryGetValue(Constants.SessionGrainOrder, out var value) && 
            value.Equals(One) &&
            !localSessionCache.Exists(sessionId: target.GrainIdentity.GetGuidKey()))
        {
            localSessionCache.Add(target.GrainIdentity.GetGuidKey(), TimeSpan.FromHours(1));
            return _cachedLocalSilo ??= Task.FromResult(context.LocalSilo);
        }

        var compatibleSilos = context.GetCompatibleSilos(target);

        if (compatibleSilos.Length == 1 && compatibleSilos[0].Equals(context.LocalSilo))
        {
            if (!localSessionCache.Exists(sessionId: target.GrainIdentity.GetGuidKey()))
            {
                return _cachedLocalSilo ??= Task.FromResult(context.LocalSilo);
            }
            
            throw new SiloUnavailableException("Not enough silos for session replication");
            // RequestContext.Set(Constants.Migrate, true);
            // logger.LogCritical("Not enough silos for session replication");
            // return _cachedLocalSilo ??= Task.FromResult(context.LocalSilo);
        }
        
        // RandomPlacementDirector without local
        if (IPlacementDirector.GetPlacementHint(target.RequestContextData, compatibleSilos) is { } placementHint)
        {
            return Task.FromResult(placementHint);
        }

        var randomSiloIndex = Random.Shared.Next(compatibleSilos.Length);

        return Task.FromResult(compatibleSilos[randomSiloIndex].Equals(context.LocalSilo) 
            ? compatibleSilos[(randomSiloIndex + 1) % compatibleSilos.Length]
            : compatibleSilos[randomSiloIndex]);
    }
}