namespace Core.Services;

public interface IPermissionService
{
    void CheckAccess(string? serviceId, IEnumerable<(string section, char action)> sectionActions);
    void CheckAccess(string? serviceId, string section, char action);
    void CheckAccess(string? serviceId, char action, IEnumerable<string> sections);
    void CheckAccess(string? serviceId, char action);
}