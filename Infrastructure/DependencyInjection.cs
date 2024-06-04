using Core.Interfaces;
using Infrastructure.Options;
using Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISessionDataRepository, SessionDataRepository>();
        
        services.AddSingleton<ILocalSessionCache, LocalSessionCache>();
        
        services.Configure<DatabaseOptions>(configuration.GetSection(nameof(DatabaseOptions)));

        return services;
    }
}