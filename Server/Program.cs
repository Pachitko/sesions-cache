using Core;
using Core.Models;
using Core.Services;
using Grains.GrainExtensions;
using Grains.States;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Orleans.Configuration;
using Orleans.Runtime;
using Server.BackgroundServices;
using Server.Extensions;
using Server.Grains;
using Server.Options;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services
    .AddOptions<ServerOptions>()
    .Bind(builder.Configuration.GetSection(nameof(ServerOptions)))
    .ValidateDataAnnotations();

builder.Services
    .AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(nameof(DatabaseOptions)))
    .ValidateDataAnnotations();

builder.Services.AddHttpClient();

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddChannel<SessionDeletion>();
builder.Services.AddChannel<SessionState>();

builder.Services.AddScoped<IPermissionService, PermissionService>();

builder.Services.AddHostedService<GrainStateUpdaterBackgroundService>();
builder.Services.AddHostedService<SessionDeleterBackgroundService>();

builder.WebHost.UseKestrelCore();
builder.WebHost.ConfigureKestrel(serverConfig =>
{
    serverConfig.AddServerHeader = false;
    serverConfig.ListenLocalhost(Random.Shared.Next(10000, 50000));
});

builder.Services.AddLogging(logging =>
{
    logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
    logging.AddSimpleConsole();

    logging.Configure(options =>
    {
        options.ActivityTrackingOptions =
            ActivityTrackingOptions.SpanId |
            ActivityTrackingOptions.TraceId |
            ActivityTrackingOptions.ParentId;
    });
});

builder.Services.AddMemoryCache();

builder.Services.AddPlacementDirector<SessionPlacementStrategy, SessionPlacementDirector>();

builder.Host.UseOrleans(static siloBuilder =>
{
    var serverOptions = siloBuilder.Configuration.GetSection(nameof(ServerOptions)).Get<ServerOptions>()!;
    var databaseOptions = siloBuilder.Configuration.GetSection(nameof(DatabaseOptions)).Get<DatabaseOptions>()!;

    siloBuilder.UseDashboard(x => { });
    
    siloBuilder.UseAdoNetClustering(options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = databaseOptions.ConnectionString;
    });

    siloBuilder.ConfigureEndpoints(serverOptions.SiloPort, serverOptions.GatewayPort);

    siloBuilder.Configure<ClusterOptions>(options =>
    {
        options.ClusterId = Constants.ClusterId;
        options.ServiceId = Constants.ServerServiceId;
    });

    siloBuilder.ConfigureServices(services =>
        services.AddSingleton<PlacementStrategy, HashBasedPlacement>());
    
    siloBuilder.AddGrainExtension<IGrainDeactivateExtension, GrainDeactivateExtension>();
});

var app = builder.Build();

await app.RunAsync();