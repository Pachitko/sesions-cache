using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using Client.Options;
using Core;
using Generated.SessionService;
using Google.Protobuf;
using Grains.Exception;
using Grains.Interfaces;
using Grains.Messages;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;

namespace Client.Services;

public sealed class SessionService(
        IMemoryCache memoryCache,
        ILogger<SessionService> logger,
        IClusterClient client, 
        IOptions<ClientOptions> clientOptions)
    : Generated.SessionService.SessionService.SessionServiceBase
{
    #region Stress
    private static readonly List<Guid> SessionIds = new();
    
    public override async Task<TestStressGetResponse> TestStressGet(
        TestStressGetRequest request, ServerCallContext context)
    {
        var elapsed = 0d;
        var cb = new ConcurrentBag<double>();
        for (var i = 0; i < request.Tests; i++)
        {
            var sw = new Stopwatch();
            sw.Start();
    
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
                        },
                        cb),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1000 }
            );

            var buffer = new BufferBlock<SessionData?>();
            updater.LinkTo(buffer);

            foreach (var sessionId in SessionIds.Take(request.Count))
            {
                // SessionIds.Add(sessionId);
                updater.Post(sessionId);
                //or await downloader.SendAsync(url);
            }

            updater.Complete();
            await updater.Completion;
    
            sw.Stop();
        
            elapsed += sw.Elapsed.TotalSeconds;
        }
    
        var orderedLatencies = cb.Order().ToList();
        Console.WriteLine(
            $"""
             Чтение
             {request.Count * request.Tests / elapsed} запросов/сек
             Прошло {elapsed / request.Tests} секунд
             50% <= {orderedLatencies[(int)(orderedLatencies.Count * 0.5)]}
             75% <= {orderedLatencies[(int)(orderedLatencies.Count * 0.75)]}
             90% <= {orderedLatencies[(int)(orderedLatencies.Count * 0.9)]}
             max latency <= {orderedLatencies[^1]}
             """);

        return new TestStressGetResponse();
    }

    public override async Task<TestStressUpdateResponse> TestStressUpdate(
        TestStressUpdateRequest request, ServerCallContext context)
    {
        var elapsed = 0d;

        var cb = new ConcurrentBag<double>();
        for (var i = 0; i < request.Tests; i++)
        {
            var sw = new Stopwatch();
            sw.Start();
        
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
                                    Value = new byte[request.Size],
                                    Version = 1
                                }
                            },
                            ExpirationUnixSeconds = DateTimeOffset.UtcNow.Add(TimeSpan.FromSeconds(300)).ToUnixTimeSeconds()
                        });

                        return true;
                    }, 
                        cb),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1000 }
            );

            var buffer = new BufferBlock<bool>();
            updater.LinkTo(buffer);

            foreach (var sessionId in Enumerable.Range(0, request.Count).Select(_ => Guid.NewGuid()))
            {
                SessionIds.Add(sessionId);
                updater.Post(sessionId);
                //or await downloader.SendAsync(url);
            }

            updater.Complete();
            await updater.Completion;
        
            sw.Stop();

            elapsed += sw.Elapsed.TotalSeconds;
        }

        var orderedLatencies = cb.Order().ToList();
        Console.WriteLine(
            $"""
            Запись
            {request.Count * request.Tests / elapsed} запросов/сек
            Прошло {elapsed / request.Tests} секунд
            50% <= {orderedLatencies[(int)(orderedLatencies.Count * 0.5)]}
            75% <= {orderedLatencies[(int)(orderedLatencies.Count * 0.75)]}
            90% <= {orderedLatencies[(int)(orderedLatencies.Count * 0.9)]}
            max latency <= {orderedLatencies[^1]}
            """);

        return new TestStressUpdateResponse();
    }
    #endregion

    public override async Task<GetSessionResponse> Get(GetSessionRequest request, ServerCallContext context)
    {
        // var c = app.Services.CreateScope().ServiceProvider.GetRequiredService<IClusterClient>();
        // var x = c.GetGrain<ITestGrain>("82F79B4E-6F64-4E79-B776-1791F9F49E84");
        // await x.Test();
        // Console.WriteLine(x.GetPrimaryKeyString());
        // return Results.Ok();
        var sessionData = await RetryWithOrder(client, memoryCache, logger, Guid.Parse(request.SessionId), async sessionGrain =>
        {
            var result = await sessionGrain.Get(new GetSessionDataQuery
            {
                Sections = request.Sections.ToArray(),
                ServiceId = null
            });
            return result;
        });

        if (sessionData is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Session not found"));
        }

        return new GetSessionResponse
        {
            ExpirationUnixSeconds = sessionData.ExpirationUnixSeconds,
            Version = sessionData.Version,
            Sections =
            {
                sessionData.Data.Select(x => new SectionResponse
                {
                    Key = x.Name,
                    Version = x.Version,
                    Data = ByteString.CopyFrom(x.Data)
                })
            }
        };
    }

    public override async Task<UpdateSessionResponse> Update(
        UpdateSessionRequest request, ServerCallContext context)
    {
        if (request.Sections.Any(s => s.Data.Length > clientOptions.Value.MaxSectionSize))
        {
            throw new SessionSizeExceededException(request.SessionId.ToString());
        }

        await RetryWithOrder(client, memoryCache, logger, Guid.Parse(request.SessionId), async sessionGrain =>
        {
            await sessionGrain.Update(new UpdateSessionCommand
            {
                Sections = request.Sections
                    .Select(x => new CreateSectionData
                    {
                        Key = x.Key,
                        Value = x.Data.ToByteArray(),
                        Version = x.Version
                    })
                    .ToArray(),
                ExpirationUnixSeconds = request.TimeToLiveInSeconds.HasValue
                    ? DateTimeOffset.UtcNow.AddSeconds(request.TimeToLiveInSeconds.Value).ToUnixTimeSeconds()
                    : null
            });

            return true;
        });

        return new UpdateSessionResponse();
    }

    public override async Task<InvalidateSessionResponse> Invalidate(InvalidateSessionRequest request, ServerCallContext context)
    {
        var ok = await RetryWithOrder(client, memoryCache, logger, Guid.Parse(request.SessionId), async sessionGrain =>
        {
            await sessionGrain.Invalidate(new InvalidateCommand
            {
                Reason = request.Reason
            });
            return true;
        });

        return ok ? new InvalidateSessionResponse() : throw new RpcException(new Status(StatusCode.NotFound, "Session not found"));
    }

    private async Task<TResult?> RetryWithOrder<TResult>(
        IGrainFactory clusterClient,
        IMemoryCache memoryCache,
        ILogger logger,
        Guid sessionId,
        Func<ISessionGrain, Task<TResult>> func,
        ConcurrentBag<double>? elapsed = null)
    {
        var order = memoryCache.TryGetValue(sessionId, out int orderValue) ? orderValue : 1;

        for (; order <= clientOptions.Value.ReplicationFactor + 1; order++)
        {
            try
            {
                var sessionGrain = clusterClient.GetGrain<ISessionGrain>(sessionId, order.ToString());
                Console.WriteLine(sessionGrain.GetPrimaryKeyString());
                RequestContext.Set(Constants.SessionGrainOrder, order);
                var sw = new Stopwatch();
                sw.Start();
                var result = await func(sessionGrain);
                sw.Stop();
                elapsed?.Add(sw.Elapsed.TotalMilliseconds);
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
}