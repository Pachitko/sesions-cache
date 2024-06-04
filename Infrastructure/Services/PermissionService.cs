using Core.Services;
using Grains.Exception;

namespace Infrastructure.Services;

public sealed class PermissionService : IPermissionService
{
    public void CheckAccess(string? serviceId, IEnumerable<(string section, char action)> sectionActions)
    {
        return;
        throw new PermissionDeniedException();
    }

    public void CheckAccess(string? serviceId, string section, char action)
    {
        return;
        throw new PermissionDeniedException();
    }

    public void CheckAccess(string? serviceId, char action, IEnumerable<string> sections)
    {
        return;
        throw new PermissionDeniedException();
    }

    public void CheckAccess(string? serviceId, char action)
    {
        return;
        throw new PermissionDeniedException();
    }
}