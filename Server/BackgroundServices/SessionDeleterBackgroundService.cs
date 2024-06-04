using System.Threading.Channels;
using Core.Interfaces;
using Core.Models;
using Microsoft.Extensions.Options;
using Server.Extensions;
using Server.Options;

namespace Server.BackgroundServices;

public sealed class SessionDeleterBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<SessionDeleterBackgroundService> logger,
        ChannelReader<SessionDeletion> sessionDeletionReader,
        IHttpClientFactory httpClientFactory,
        IOptions<ServerOptions> serverOptions)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var sessionIdsBatch in sessionDeletionReader
                           .ReadAllBatches(batchSize: 500, timeout: TimeSpan.FromMilliseconds(1000))
                           .WithCancellation(stoppingToken))
        {
            try
            {
                await using var scope = serviceScopeFactory.CreateAsyncScope();
                var sessionDataRepository = scope.ServiceProvider.GetRequiredService<ISessionDataRepository>();
                await sessionDataRepository.Delete(sessionIdsBatch, stoppingToken);
                
                if (!string.IsNullOrWhiteSpace(serverOptions.Value.InvalidationCallbackUrl))
                {
                    var client = httpClientFactory.CreateClient();
                    await client.PostAsJsonAsync(
                        serverOptions.Value.InvalidationCallbackUrl,
                        sessionIdsBatch.Select(x => new
                        {
                            x.SessionId,
                            x.Reason,
                            DateTimeOffset.UtcNow
                        }), 
                        stoppingToken);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Delete sessions error");
            }
        }
    }
}