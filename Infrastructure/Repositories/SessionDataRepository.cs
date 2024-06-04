using System.Transactions;
using Core.Interfaces;
using Core.Models;
using Dapper;
using Infrastructure.Options;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Infrastructure.Repositories;

internal sealed class SessionDataRepository(
        IOptions<DatabaseOptions> databaseOptions) 
    : ISessionDataRepository
{
    public async Task CreateSessions(IEnumerable<Guid> sessionIds, CancellationToken token)
    {
        await using var connection = new NpgsqlConnection(databaseOptions.Value.ConnectionString);
        await connection.OpenAsync(token);

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
            sessionIds.Select(sessionId => new
            {
                SessionId = sessionId
            }));
    }

    public async Task<bool> Exists(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(databaseOptions.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        
        if (Transaction.Current is not null &&
            Transaction.Current.TransactionInformation.Status is TransactionStatus.Aborted)
        {
            throw new TransactionAbortedException("Transaction was aborted (probably by user cancellation request)");
        }
        
        var exists = await connection.ExecuteScalarAsync<bool>("select count(1) from sessions where id=@id",
            new 
            {
                id = sessionId
            },
            commandTimeout: 1000);

        return exists;
    }

    public async Task Delete(IEnumerable<SessionDeletion> sessionsToDelete, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(databaseOptions.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        
        if (Transaction.Current is not null &&
            Transaction.Current.TransactionInformation.Status is TransactionStatus.Aborted)
        {
            throw new TransactionAbortedException("Transaction was aborted (probably by user cancellation request)");
        }
        
        await connection.ExecuteAsync(
            "delete from sessions where id = any(@SessionIds)",
            new
            {
                SessionIds = sessionsToDelete
                    .Select(sessionId => sessionId.SessionId)
                    .ToArray() 
            });
    }
}