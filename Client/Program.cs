using System.Security.AccessControl;
using Client.Contracts.Requests;
using Client.Middlewares;
using Core;
using Grains.Exception;
using Grains.Interfaces;
using Grains.Messages;
using Microsoft.AspNetCore.Mvc;using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Messaging;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;
using SessionOptions = Client.Options.SessionOptions;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddMemoryCache();

builder.Services.Configure<SessionOptions>(builder.Configuration.GetSection(nameof(SessionOptions)));

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.AddServerHeader = false;
});

builder.Host.UseOrleansClient(static clientBuilder =>
{
    clientBuilder.AddMemoryStreams(Constants.StreamProvider);
    
#if DEBUG
    clientBuilder.UseAdoNetClustering(options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = "User ID=postgres;Password=12qwasZX;Host=localhost;Port=5433;Database=session_cluster";
    });
#else
    clientBuilder.UseAdoNetClustering(options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = "User ID=postgres;Password=12qwasZX;Host=localhost;Port=5433;Database=session_cluster";
    });
#endif

    clientBuilder.Configure<GatewayOptions>(options =>
    {
        options.GatewayListRefreshPeriod = TimeSpan.FromSeconds(10);
    });
    
    clientBuilder.Configure<ClusterOptions>(options =>
    {
        options.ClusterId = Constants.ClusterId;
        options.ServiceId = "sessions-client";
    });
});

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

var sessionsGroup = app.MapGroup("/sessions/");

var grainIds = new List<Guid>();

sessionsGroup.MapGet("/stateless-grain", async ([FromServices] IClusterClient client) =>
{
    var streamGuid = Guid.NewGuid();

    var streamProvider = client.GetStreamProvider(Constants.StreamProvider);

    var streamId = StreamId.Create(Constants.SessionStreamNamespace, streamGuid);
    var stream = streamProvider.GetStream<int>(streamId);

    await stream.OnNextAsync(123);
    
    // var replicatedGrain = client.GetGrain<IStatelessGrain>();

    return Results.Json(new
    {
        Value = 0//await replicatedGrain.Get("qwe")
    });
});

sessionsGroup.MapGet("/replicated-grain", async ([FromServices] IClusterClient client) =>
{
    var replicatedGrain = client.GetGrain<IReplicatedGrain>(Guid.NewGuid());

    await replicatedGrain.Add();
    
    return Results.Json(new
    {
        Value = await replicatedGrain.Get()
    });
});

sessionsGroup.MapPost("stress/{count}", async (int count, [FromServices] IClusterClient client) =>
{
    RequestContext.Set(Constants.SessionGrainOrder, 1);

    var tasks = new List<Task>();
    for (var i = 0; i < count; i++)
    {
        var grainId = Guid.NewGuid();
        grainIds.Add(grainId);
        var sessionGrain = client.GetGrain<ISessionGrain>(grainId, "0");
        var task = sessionGrain.Update(new UpdateSessionCommand
        {
            Sections = new []
            {
                new SectionData
                {
                    Key = "some key",
                    Value = new byte[10000],
                    Version = 0
                }
            },
            ExpirationUnixSeconds = DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(5)).ToUnixTimeSeconds()
        });
        
        tasks.Add(task);
    }

    await Task.WhenAll(tasks);
    return Results.Ok();
});


sessionsGroup.MapPatch("stress/{count}", async (
    [FromRoute] int count,
    [FromServices] IClusterClient client) =>
{
    RequestContext.Set("client", null);
    RequestContext.Set("next-grain-order", 1);
    
    var tasks = new List<Task>();
    for (var i = 0; i < count; i++)
    {
        var grainId = grainIds[i];
        var sessionGrain = client.GetGrain<ISessionGrain>(grainId, "0");
        var task = sessionGrain.Update(new UpdateSessionCommand
        {
            Sections = new []
            {
                new SectionData
                {
                    Key = "some key",
                    Value = new byte[10000],
                    Version = 0
                }
            },
            ExpirationUnixSeconds = DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(5)).ToUnixTimeSeconds()
        });
        
        tasks.Add(task);
    }

    await Task.WhenAll(tasks);
    return Results.Ok();
});

sessionsGroup.MapGet("stress/{count}", async ( 
    [FromRoute] int count,
    [FromServices] IClusterClient client) =>
{
    RequestContext.Set("client", null);
    var tasks = new List<Task>();
    for (var i = 0; i < count; i++)
    {
        var sessionGrain = client.GetGrain<ISessionGrain>(grainIds[i], "1");
        var task = sessionGrain.Get();
        tasks.Add(task);
    }

    await Task.WhenAll(tasks);
    return Results.Ok();
});

sessionsGroup.MapGet("{sessionId}", async (
    [FromRoute] Guid sessionId,
    [FromServices] IClusterClient client,
    [FromServices] IMemoryCache memoryCache,
    [FromServices] ILogger<Program> logger) =>
{    
    var order = memoryCache.TryGetValue(sessionId, out int orderValue) ? orderValue : 1;
    for (; order <= 2; order++)
    {
        try
        {
            RequestContext.Set(Constants.SessionGrainOrder, order);

            var sessionGrain = client.GetGrain<ISessionGrain>(sessionId, order.ToString());
            var sessionData = await sessionGrain.Get();

            // if (order == 0)
            // {
            //     await client.GetGrain<ISessionGrain>(sessionId, (order + 1).ToString()).Get();
            // }
            
            memoryCache.Set(sessionId, order, TimeSpan.FromSeconds(30));

            return Results.Json(sessionData);
        }
        catch (Exception e)
        {
            logger.LogError(e, "{Message}", e.Message);
        }
    }

    return Results.Ok();
});

sessionsGroup.MapPatch("/", async (
    [FromBody] UpdateSessionRequest request,
    [FromServices] IMemoryCache memoryCache,
    [FromServices] ILogger<Program> logger,
    [FromServices] IClusterClient client,
    [FromServices] IGatewayListProvider gatewayListProvider,
    IOptions<SessionOptions> options) =>
{
    // var silos = await gatewayListProvider.GetGateways();
    // var randomSiloUri = silos[Random.Shared.Next(silos.Count)];
    // var siloAddress = SiloAddress.New(randomSiloUri.ToIPEndPoint()!, 0);
    // RequestContext.Set(IPlacementDirector.PlacementHintKey, siloAddress);

    if (request.Sections.Any(s => s.Value.Length > options.Value.MaxSectionSize))
    {
        throw new SessionSizeExceededException(request.SessionId.ToString());
    }

    await RetryWithOrder(memoryCache, logger, request.SessionId, async order =>
    {
        RequestContext.Set(Constants.SessionGrainOrder, order);

        var sessionGrain = client.GetGrain<ISessionGrain>(request.SessionId, order.ToString());

        await sessionGrain.Update(new UpdateSessionCommand
        {
            Sections = request.Sections
                .Select(x => new SectionData
                {
                    Key = x.Key,
                    Value = x.Value,
                    Version = x.Version
                })
                .ToArray(),
            ExpirationUnixSeconds = request.TimeToLive.HasValue
                ? DateTimeOffset.UtcNow.Add(request.TimeToLive.Value).ToUnixTimeSeconds()
                : null
        });
    });

    return Results.Ok();
});

sessionsGroup.MapDelete("{sessionId}", async (
    Guid sessionId,
    [FromServices] IMemoryCache memoryCache,
    [FromServices] ILogger<Program> logger,
    [FromServices] IClusterClient client) =>
{
    var ok = await RetryWithOrder(memoryCache, logger, sessionId, async (order) =>
    {
        RequestContext.Set(Constants.SessionGrainOrder, order + 1);

        var sessionGrain = client.GetGrain<ISessionGrain>(sessionId, order.ToString());
        await sessionGrain.Invalidate();

        memoryCache.Set(sessionId, order, TimeSpan.FromSeconds(30));
    });
    
    return ok ? Results.Ok() : Results.BadRequest();
});

await app.RunAsync();
return;

static async Task<bool> RetryWithOrder(IMemoryCache memoryCache, ILogger<Program> logger, Guid sessionId, Func<int, Task> func)
{
    var order = memoryCache.TryGetValue(sessionId, out int orderValue) ? orderValue : 1;

    for (; order <= 2; order++)
    {
        try
        {
            await func(order);
            return true;
        }
        catch (Exception e) when (e is TimeoutException or SiloUnavailableException)
        {
            logger.LogError(e, "{Message}", e.Message);
        }
    }
    
    return false;
}