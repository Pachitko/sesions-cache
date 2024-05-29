using System.Threading.Channels;
using Core;
using Grains.GrainExtensions;
using Grains.States;
using Infrastructure;
using Orleans.Configuration;
using Orleans.Runtime;
using Server.BackgroundServices;
using Server.Grains;
using Server.Options;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

var sessionGrainStateChannel = Channel.CreateUnbounded<SessionState>(new UnboundedChannelOptions
{
    AllowSynchronousContinuations = true,
    SingleReader = true,
    SingleWriter = false
});

builder.Services.AddSingleton<ChannelReader<SessionState>>(sessionGrainStateChannel);
builder.Services.AddSingleton<ChannelWriter<SessionState>>(sessionGrainStateChannel);

builder.Services.AddHostedService<GrainStateUpdaterBackgroundService>();

builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(nameof(DatabaseOptions)));

builder.WebHost.UseKestrelCore();
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.AddServerHeader = false;
    serverOptions.ListenLocalhost(Random.Shared.Next(10000, 50000));
});

builder.Services.AddLogging(logging =>
{
    logging.SetMinimumLevel(LogLevel.None);
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
    siloBuilder
        .AddMemoryStreams(Constants.StreamProvider)
        .AddMemoryGrainStorage("PubSubStore");
    
    // siloBuilder.AddStateStorageBasedLogConsistencyProvider();
    // siloBuilder.AddLogStorageBasedLogConsistencyProvider();

    siloBuilder.UseDashboard(x => { });
    
#if DEBUG
    siloBuilder.UseAdoNetClustering(options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = "User ID=postgres;Password=12qwasZX;Host=localhost;Port=5433;Database=session_cluster";
    });
#else
    siloBuilder.UseAdoNetClustering(options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = "User ID=postgres;Password=12qwasZX;Host=localhost;Port=5433;Database=session_cluster";
    });
#endif

    siloBuilder.ConfigureEndpoints(siloPort: Random.Shared.Next(10000, 50000), gatewayPort: Random.Shared.Next(10000, 50000));

    siloBuilder.Configure<ClusterOptions>(options =>
    {
        options.ClusterId = "session-cluster";
        options.ServiceId = "sessions-server";
    });
    
    siloBuilder.AddAdoNetGrainStorage("StreamProvider", options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString =
            "User ID=postgres;Password=12qwasZX;Host=localhost;Port=5433;Database=session_cluster";
    });
    
    siloBuilder.AddAdoNetGrainStorage("PubSubStore", options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString =
            "User ID=postgres;Password=12qwasZX;Host=localhost;Port=5433;Database=session_cluster";
    });
    
    siloBuilder.AddAdoNetGrainStorage("StateStorage", options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString =
            "User ID=postgres;Password=12qwasZX;Host=localhost;Port=5433;Database=session_cluster";
    });
     
    siloBuilder.AddAdoNetGrainStorage("LogStorage", options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString =
            "User ID=postgres;Password=12qwasZX;Host=localhost;Port=5433;Database=session_cluster";
    });
    
    siloBuilder.AddAdoNetGrainStorage("sessions", options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString =
            "User ID=postgres;Password=12qwasZX;Host=localhost;Port=5433;Database=session_cluster";
    });
    
    siloBuilder.ConfigureServices(services =>
        services.AddSingleton<PlacementStrategy, HashBasedPlacement>());
    
    siloBuilder.AddGrainExtension<IGrainDeactivateExtension, GrainDeactivateExtension>();
});

var app = builder.Build();

await app.RunAsync();