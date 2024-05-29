namespace Core.Interfaces;

public interface ILocalSessionCache
{
    void Add(Guid sessionId, TimeSpan expirationTime);
    void Remove(Guid sessionId);
    bool Exists(Guid sessionId);
}