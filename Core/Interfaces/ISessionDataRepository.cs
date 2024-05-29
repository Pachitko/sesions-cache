namespace Core.Interfaces;

public interface ISessionDataRepository
{
    Task<bool> Exists(Guid sessionId, CancellationToken cancellationToken);
    Task Delete(Guid sessionId, CancellationToken cancellationToken);
}