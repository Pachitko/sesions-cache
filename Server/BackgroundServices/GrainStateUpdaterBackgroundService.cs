using System.Threading.Channels;
using System.Transactions;
using Core;
using Dapper;
using Grains.States;
using Microsoft.Extensions.Options;
using Npgsql;
using Server.Extensions;
using Server.Options;

namespace Server.BackgroundServices;

internal sealed class GrainStateUpdaterBackgroundService(
        IOptions<DatabaseOptions> databaseOptions,
        ILogger<GrainStateUpdaterBackgroundService> logger,
        ChannelReader<SessionState> sessionGrainStateReader)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var sessionStateBatch in sessionGrainStateReader
                           .ReadAllBatches(batchSize: 1000, timeout: TimeSpan.FromMilliseconds(500))
                           .WithCancellation(stoppingToken))
        {
            try
            {
                await using var connection = new NpgsqlConnection(databaseOptions.Value.SessionDbConnectionString);
                await connection.OpenAsync(stoppingToken);

                if (Transaction.Current is not null &&
                    Transaction.Current.TransactionInformation.Status is TransactionStatus.Aborted)
                {
                    throw new TransactionAbortedException("Transaction was aborted (probably by user cancellation request)");
                }

                const string sql = 
                    """
                    insert into sessions (id)
                    values (@SessionId)
                        on conflict do nothing 
                    """;
                
                await connection.ExecuteAsync(
                    sql,
                    sessionStateBatch.Select(s => new SessionData
                    {
                        SessionId = s.Id
                    }));
            }
            catch (Exception e)
            {
                logger.LogError(e, "Write state error");
            }
        }
    }
}