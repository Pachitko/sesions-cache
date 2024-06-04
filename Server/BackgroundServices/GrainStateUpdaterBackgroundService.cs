using System.Threading.Channels;
using Core.Interfaces;
using Grains.States;
using Server.Extensions;

namespace Server.BackgroundServices;

internal sealed class GrainStateUpdaterBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<GrainStateUpdaterBackgroundService> logger,
        ChannelReader<SessionState> sessionGrainStateReader)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var sessionStateBatch in sessionGrainStateReader
                           .ReadAllBatches(batchSize: 5_000, timeout: TimeSpan.FromMilliseconds(1000))
                           .WithCancellation(stoppingToken))
        {
            try
            {
                await using var scope = serviceScopeFactory.CreateAsyncScope();
                var sessionDataRepository = scope.ServiceProvider.GetRequiredService<ISessionDataRepository>();
                await sessionDataRepository.CreateSessions(sessionStateBatch.Select(x => x.Id), stoppingToken);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Write state error");
            }
        }
    }
}