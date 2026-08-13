namespace GaoApp.Application.Interfaces.Common;

public interface IInventoryPostingTransactionCoordinator
{
    bool HasActiveTransaction { get; }

    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default);
}
