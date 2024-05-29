using System.Transactions;
using Core.Interfaces;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using Server.Options;
using IsolationLevel = System.Data.IsolationLevel;

namespace Infrastructure.Repositories;

internal sealed class SessionDataRepository(
        IOptions<DatabaseOptions> databaseOptions) 
    : ISessionDataRepository
{
    public async Task<bool> Exists(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(databaseOptions.Value.SessionDbConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        
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

    public async Task Delete(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(databaseOptions.Value.SessionDbConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        
        if (Transaction.Current is not null &&
            Transaction.Current.TransactionInformation.Status is TransactionStatus.Aborted)
        {
            throw new TransactionAbortedException("Transaction was aborted (probably by user cancellation request)");
        }
        
        await connection.ExecuteAsync("delete from sessions where id=@id",
            new 
            {
                id = sessionId
            },
            commandTimeout: 1000);
    }
}