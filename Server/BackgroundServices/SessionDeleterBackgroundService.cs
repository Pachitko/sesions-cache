using System.Threading.Channels;
using Core.Interfaces;
using Core.Models;
using Server.Extensions;

namespace Server.BackgroundServices;

public sealed class SessionDeleterBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<SessionDeleterBackgroundService> logger,
        ChannelReader<SessionDeletion> sessionDeletionReader)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var sessionIdsBatch in sessionDeletionReader
                           .ReadAllBatches(batchSize: 1000, timeout: TimeSpan.FromMilliseconds(1000))
                           .WithCancellation(stoppingToken))
        {
            try
            {
                await using var scope = serviceScopeFactory.CreateAsyncScope();
                var sessionDataRepository = scope.ServiceProvider.GetRequiredService<ISessionDataRepository>();
                await sessionDataRepository.Delete(sessionIdsBatch, stoppingToken);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Delete sessions error");
            }
        }
    }
}