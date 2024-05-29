using System.Runtime.CompilerServices;
using Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure;

internal sealed class LocalSessionCache(IMemoryCache memoryCache) : ILocalSessionCache
{
    private const string SessionPrefix = "session_";

    public void Add(Guid sessionId, TimeSpan expirationTime)
    {
        memoryCache.Set(GetKey(sessionId), new object(), expirationTime);
    }

    public void Remove(Guid sessionId)
    {
        memoryCache.Remove(GetKey(sessionId));
    }

    public bool Exists(Guid sessionId)
    {
        return memoryCache.TryGetValue(GetKey(sessionId), out _);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetKey(Guid sessionId)
    {
        return SessionPrefix + sessionId;
    }
}