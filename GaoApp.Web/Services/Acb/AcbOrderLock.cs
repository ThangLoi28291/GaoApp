using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

/// <summary>Session lock spans the existing POS finalization transaction; works across web instances.</summary>
public sealed class AcbOrderLock : IAsyncDisposable
{
    private readonly AppDbContext _db;
    private readonly string _resource;
    private AcbOrderLock(AppDbContext db, string resource) { _db = db; _resource = resource; }
    public static async Task<AcbOrderLock> AcquireAsync(AppDbContext db, int orderId, CancellationToken ct)
    {
        var handle = new AcbOrderLock(db, $"acb:{db.CurrentStoreId}:order:{orderId}");
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={handle._resource}, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=15000; IF @result < 0 THROW 51000, 'Payment is being processed. Please retry.', 1;", ct);
            return handle;
        }
        catch { await db.Database.CloseConnectionAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        try { await _db.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_releaseapplock @Resource={_resource}, @LockOwner='Session';"); }
        finally { await _db.Database.CloseConnectionAsync(); }
    }
}
