using Core.Models;

namespace Core.Interfaces;

public interface ISessionDataRepository
{
    Task<bool> Exists(Guid sessionId, CancellationToken cancellationToken);
    Task Delete(IEnumerable<SessionDeletion> sessionsToDelete, CancellationToken cancellationToken);
}