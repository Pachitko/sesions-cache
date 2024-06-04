using System.Threading.Tasks.Dataflow;
using Client.Contracts.Requests;
using Client.Middlewares;
using Client.Options;
using Core;
using Grains.Exception;
using Grains.Interfaces;
using Grains.Messages;
using Microsoft.AspNetCore.Mvc;using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddMemoryCache();

builder.Services
    .AddOptions<ClientOptions>()
    .Bind(builder.Configuration.GetSection(nameof(ClientOptions)))
    .ValidateDataAnnotations();

var clientOptions = builder.Configuration.GetSection(nameof(ClientOptions)).Get<ClientOptions>()!;

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.AddServerHeader = false;
});

builder.Host.UseOrleansClient(clientBuilder =>
{
    clientBuilder.UseAdoNetClustering(options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = clientOptions.ConnectionString;
    });

    clientBuilder.Configure<GatewayOptions>(options =>
    {
        options.GatewayListRefreshPeriod = TimeSpan.FromSeconds(10);
    });
    
    clientBuilder.Configure<ClusterOptions>(options =>
    {
        options.ClusterId = Constants.ClusterId;
        options.ServiceId = Constants.ClientServiceId;
    });
});

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

var sessionsGroup = app.MapGroup("/sessions/");

#region stress
var sessionIds = new List<Guid>();

sessionsGroup.MapPatch("stress/{count}/{size}", async (
    [FromRoute] int count,
    [FromRoute] int size,
    [FromServices] IMemoryCache memoryCache,
    [FromServices] ILogger<Program> logger,
    [FromServices] IClusterClient client) =>
{
    var updater = new TransformBlock<Guid, bool>(
        sessionId =>
            RetryWithOrder(client, memoryCache, logger, sessionId, async sessionGrain =>
            {
                await sessionGrain.Update(new UpdateSessionCommand
                {
                    Sections = new[]
                    {
                        new CreateSectionData
                        {
                            Key = "some key",
                            Value = new byte[size],
                            Version = 1
                        }
                    },
                    ExpirationUnixSeconds = DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(1)).ToUnixTimeSeconds()
                });

                return true;
            }),
        new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1000 }
    );

    var buffer = new BufferBlock<bool>();
    updater.LinkTo(buffer);

    foreach (var sessionId in Enumerable.Range(0, count).Select(_ => Guid.NewGuid()))
    {
        sessionIds.Add(sessionId);
        updater.Post(sessionId);
        //or await downloader.SendAsync(url);
    }

    updater.Complete();
    await updater.Completion;
    
    return Results.Ok();
});

sessionsGroup.MapGet("stress/{count}", async ( 
    [FromRoute] int count,
    [FromServices] IMemoryCache memoryCache,
    [FromServices] ILogger<Program> logger,
    [FromServices] IClusterClient client) =>
{
    var updater = new TransformBlock<Guid, SessionData?>(
        sessionId =>
            RetryWithOrder(client, memoryCache, logger, sessionId, async sessionGrain =>
            {
                var result = await sessionGrain.Get(new GetSessionDataQuery
                {
                    Sections = new []
                    {
                        "some key"
                    }
                });
                return result;
            }),
        new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1000 }
    );

    var buffer = new BufferBlock<SessionData?>();
    updater.LinkTo(buffer);

    foreach (var sessionId in sessionIds.Take(count))
    {
        sessionIds.Add(sessionId);
        updater.Post(sessionId);
        //or await downloader.SendAsync(url);
    }

    updater.Complete();
    await updater.Completion;
    
    return Results.Ok();
});
#endregion

sessionsGroup.MapPost("get", async (
    [FromBody] GetSessionRequest request,
    [FromServices] IClusterClient client,
    [FromServices] IMemoryCache memoryCache,
    [FromServices] ILogger<Program> logger) =>
{
    var sessionData = await RetryWithOrder(client, memoryCache, logger, request.SessionId, async sessionGrain =>
    {
        var result = await sessionGrain.Get(new GetSessionDataQuery
        {
            Sections = request.Sections
        });
        return result;
    });

    return sessionData is null
        ? Results.NotFound()
        : Results.Json(sessionData);
});

sessionsGroup.MapPatch("/", async (
    [FromBody] UpdateSessionRequest request,
    [FromServices] IMemoryCache memoryCache,
    [FromServices] ILogger<Program> logger,
    [FromServices] IClusterClient client,
    IOptions<ClientOptions> options) =>
{
    if (request.Sections.Any(s => s.Value.Length > options.Value.MaxSectionSize))
    {
        throw new SessionSizeExceededException(request.SessionId.ToString());
    }

    await RetryWithOrder(client, memoryCache, logger, request.SessionId, async sessionGrain =>
    {
        await sessionGrain.Update(new UpdateSessionCommand
        {
            Sections = request.Sections
                .Select(x => new CreateSectionData
                {
                    Key = x.Key,
                    Value = x.Value,
                    Version = x.Version
                })
                .ToArray(),
            ExpirationUnixSeconds = request.TimeToLive.HasValue
                ? DateTimeOffset.UtcNow.AddSeconds(request.TimeToLive.Value).ToUnixTimeSeconds()
                : null
        });

        return true;
    });

    return Results.Ok();
});

sessionsGroup.MapDelete("{sessionId}", async (
    [FromRoute] Guid sessionId,
    [FromQuery] string reason,
    [FromServices] IMemoryCache memoryCache,
    [FromServices] ILogger<Program> logger,
    [FromServices] IClusterClient client) =>
{
    var ok = await RetryWithOrder(client, memoryCache, logger, sessionId, async sessionGrain =>
    {
        await sessionGrain.Invalidate(new InvalidateCommand
        {
            Reason = reason
        });
        return true;
    });
    
    return ok ? Results.Ok() : Results.BadRequest();
});

await app.RunAsync();
return;

async Task<TResult?> RetryWithOrder<TResult>(
    IGrainFactory clusterClient,
    IMemoryCache memoryCache,
    ILogger logger,
    Guid sessionId,
    Func<ISessionGrain, Task<TResult>> func)
{
    var order = memoryCache.TryGetValue(sessionId, out int orderValue) ? orderValue : 1;

    for (; order <= clientOptions.ReplicationFactor; order++)
    {
        try
        {
            var sessionGrain = clusterClient.GetGrain<ISessionGrain>(sessionId, order.ToString());
            RequestContext.Set(Constants.SessionGrainOrder, order);
            var result = await func(sessionGrain);
            memoryCache.Set(sessionId, order, TimeSpan.FromSeconds(5));
            return result;
        }
        catch (Exception e) when (e is TimeoutException or SiloUnavailableException or ConnectionFailedException)
        {
            logger.LogError(e, "Retry: {Message}", e.Message);
        }
        catch (Exception e) when (e is SessionExpiredException)
        {
            return default;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unknown: {Message}", e.Message);
        }
    }
    
    return default;
}