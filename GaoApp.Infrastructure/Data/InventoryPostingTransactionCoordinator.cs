using System.Data;
using GaoApp.Application.Common;
using GaoApp.Application.Interfaces.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Data;

public sealed class InventoryPostingTransactionCoordinator
    : IInventoryPostingTransactionCoordinator
{
    private const string IdempotencyIndexName =
        "UX_InventoryTransactions_StoreId_IdempotencyKey_Active";

    private const string BalanceIndexName =
        "IX_InventoryBalances_StoreId_WarehouseId_ProductVariantId";

    private readonly AppDbContext _db;

    public InventoryPostingTransactionCoordinator(AppDbContext db)
    {
        _db = db;
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (_db.Database.CurrentTransaction is not null)
        {
            try
            {
                return await operation(ct);
            }
            catch (Exception exception)
            {
                var translated = Translate(exception);
                if (ReferenceEquals(translated, exception))
                {
                    throw;
                }

                throw translated;
            }
        }

        await using var transaction = await _db.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            var result = await operation(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception exception)
        {
            await TryRollbackAsync(transaction, exception);
            var translated = Translate(exception);
            if (ReferenceEquals(translated, exception))
            {
                throw;
            }

            throw translated;
        }
    }

    private static Exception Translate(Exception exception)
    {
        if (exception is ConcurrencyException
            || exception is OperationCanceledException)
        {
            return exception;
        }

        var sqlException = FindSqlException(exception);
        if (sqlException is null)
        {
            return exception;
        }

        if (sqlException.Number is 1205 or 1222 or -2)
        {
            return new ConcurrencyException(
                "Inventory posting could not acquire its required SQL Server lock.",
                exception);
        }

        if (sqlException.Number is 2601 or 2627
            && (sqlException.Message.Contains(
                    IdempotencyIndexName,
                    StringComparison.Ordinal)
                || sqlException.Message.Contains(
                    BalanceIndexName,
                    StringComparison.Ordinal)))
        {
            return new ConcurrencyException(
                "A concurrent inventory posting won the same durable identity.",
                exception);
        }

        return exception;
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is SqlException sqlException)
            {
                return sqlException;
            }
        }

        return null;
    }

    private static async Task TryRollbackAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        Exception postingException)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollbackException)
        {
            postingException.Data[
                "InventoryPostingRollbackException"] =
                rollbackException;
        }
    }
}
