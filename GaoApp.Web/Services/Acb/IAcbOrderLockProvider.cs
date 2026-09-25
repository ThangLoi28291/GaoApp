using GaoApp.Infrastructure.Data;

namespace GaoApp.Web.Services.Acb;

public interface IAcbOrderLockProvider
{
    Task<IAsyncDisposable> AcquireAsync(AppDbContext db, int orderId, CancellationToken ct);
}

public sealed class SqlAcbOrderLockProvider : IAcbOrderLockProvider
{
    public async Task<IAsyncDisposable> AcquireAsync(AppDbContext db, int orderId, CancellationToken ct) =>
        await AcbOrderLock.AcquireAsync(db, orderId, ct);
}
