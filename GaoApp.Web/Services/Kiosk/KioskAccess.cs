using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace GaoApp.Web.Services.Kiosk;

public sealed class KioskAccess(AppDbContext db, IHttpContextAccessor http)
{
    public const string Cookie = "GAO_KIOSK_DEVICE";
    public int StoreId => db.CurrentStoreId ?? 0;
    public static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public async Task<KioskStation?> FindAsync(CancellationToken ct)
    {
        var key = http.HttpContext?.Request.Cookies[Cookie];
        if (StoreId <= 0 || key?.Length != 64) return null;
        var hash = Hash(key);
        return await db.Set<KioskStation>().Include(x => x.Terminal).Include(x => x.Warehouse)
            .SingleOrDefaultAsync(x => x.StoreId == StoreId && !x.IsDeleted && x.IsActive && x.DeviceHash == hash &&
                x.Terminal.IsActive && !x.Terminal.IsDeleted && x.Terminal.Status == POSTerminalStatus.Active &&
                x.Warehouse.IsActive && !x.Warehouse.IsDeleted, ct);
    }
    // A request-only system identity has no roles or permissions and is never signed into admin cookies.
    public IDisposable Enter(KioskStation station)
    {
        var context = http.HttpContext!;
        var previous = context.User;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, station.SystemUserId.ToString()),
            new Claim(ClaimTypes.Name, "Quầy tự phục vụ " + station.Terminal.Code),
            new Claim("terminal_id", station.TerminalId.ToString()),
            new Claim("terminal_code", station.Terminal.Code),
            new Claim("store_id", station.StoreId.ToString()) }, "KioskInternal"));
        var old = new[] { "CurrentTerminalId", "CurrentTerminalName", "CurrentTerminalCode" }
            .ToDictionary(k => k, k => context.Items[k]);
        context.Items["CurrentTerminalId"] = station.TerminalId;
        context.Items["CurrentTerminalName"] = station.Terminal.Name;
        context.Items["CurrentTerminalCode"] = station.Terminal.Code;
        return new Restore(() => { context.User = previous; foreach (var pair in old) context.Items[pair.Key] = pair.Value; });
    }
    private sealed class Restore(Action action) : IDisposable { public void Dispose() => action(); }
}

/// <summary>Serializes device commands across web instances, including bank calls and POS transactions.</summary>
public sealed class KioskLock(AppDbContext db, string resource) : IAsyncDisposable
{
    public static async Task<KioskLock> AcquireAsync(AppDbContext db, int stationId, CancellationToken ct)
    {
        var gate = new KioskLock(db, $"kiosk:{db.CurrentStoreId}:{stationId}");
        await db.Database.OpenConnectionAsync(ct);
        try {
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={gate.resource}, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=15000; IF @r<0 THROW 51000, 'Quay dang xu ly. Vui long thu lai.', 1;", ct);
            return gate;
        } catch { await db.Database.CloseConnectionAsync(); throw; }
    }
    private readonly string resource = resource;
    public async ValueTask DisposeAsync()
    {
        try { await db.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_releaseapplock @Resource={resource}, @LockOwner='Session';"); }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
