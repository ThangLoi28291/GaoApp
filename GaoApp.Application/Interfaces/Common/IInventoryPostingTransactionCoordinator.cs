namespace GaoApp.Application.Interfaces.Common;

public interface IInventoryPostingTransactionCoordinator
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default);
}
