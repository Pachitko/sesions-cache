using Core.Interfaces;
using Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.Options;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISessionDataRepository, SessionDataRepository>();
        
        services.AddSingleton<ILocalSessionCache, LocalSessionCache>();
        
        services.Configure<DatabaseOptions>(configuration.GetSection(nameof(DatabaseOptions)));

        return services;
    }
}