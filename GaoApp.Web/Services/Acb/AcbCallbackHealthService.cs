using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.Acb;

public sealed class AcbCallbackHealthService(AppDbContext db, AcbProtocol protocol,
    AcbCallbackDiagnostics diagnostics, IOptions<AcbCallbackRoutingOptions> options)
{
    public async Task<AcbCallbackHealth> ReadAsync(int storeId, string currentUrl, bool canManageRouting, CancellationToken ct)
    {
        if (storeId <= 0 || db.CurrentStoreId != storeId) throw new UnauthorizedAccessException();
        var settings = await db.Set<StoreAcbSettings>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == storeId, ct);
        var keyState = "Missing";
        if (!string.IsNullOrWhiteSpace(settings?.CallbackApiKeyProtected))
        {
            try { keyState = string.IsNullOrWhiteSpace(protocol.Unprotect(storeId, settings.CallbackApiKeyProtected)) ? "Missing" : "Ready"; }
            catch (System.Security.Cryptography.CryptographicException) { keyState = "Unreadable"; }
        }
        var hosts = options.Value.Hosts.Select(x => x.Trim().ToLowerInvariant()).ToArray();
        var routes = await db.Set<AcbCallbackRoute>().AsNoTracking().Where(x => hosts.Contains(x.Host)).ToListAsync(ct);
        var endpoints = hosts.Select(host =>
        {
            var route = routes.SingleOrDefault(x => x.Host == host);
            return new AcbCallbackHealthRoute($"https://{host}/Admin/api-callback",
                route?.TargetStoreId == storeId ? "CurrentStore" : route?.TargetStoreId == null ? "NotSelected" : "OtherStore");
        }).ToArray();
        var receipts = db.Set<AcbCallbackReceipt>().AsNoTracking().Where(x => x.StoreId == storeId);
        var totals = await receipts.GroupBy(x => 1).Select(g => new
        {
            LastReceived = g.Max(x => (DateTime?)x.CreatedAtUtc), Pending = g.Count(x => x.ProcessedAtUtc == null), Review = g.Count(x => x.NeedsReview)
        }).SingleOrDefaultAsync(ct);
        var recent = await receipts.OrderByDescending(x => x.Id).Take(20)
            .Select(x => new AcbCallbackReceiptSummary(x.Id, x.Page, x.Attempts, x.CreatedAtUtc,
                x.ProcessedAtUtc, x.NextAttemptAtUtc, x.NeedsReview, x.LastErrorCode, x.RequestCode, x.TotalPages)).ToListAsync(ct);
        // Unassigned host errors are visible only to a verified host administrator.
        var unassigned = canManageRouting ? hosts.Where(host => !routes.Any(x => x.Host == host && x.TargetStoreId.HasValue)).ToArray() : [];
        return new(currentUrl, settings?.Enabled == true, keyState, canManageRouting, endpoints,
            totals?.LastReceived, totals?.Pending ?? 0, totals?.Review ?? 0, recent,
            diagnostics.RecentForStore(storeId, unassigned), diagnostics.StartedAtUtc);
    }
}

public sealed record AcbCallbackHealthRoute(string Url, string State);
public sealed record AcbCallbackHealth(string CurrentUrl, bool Enabled, string KeyState, bool CanManageRouting,
    IReadOnlyList<AcbCallbackHealthRoute> Routes, DateTime? LastReceivedAtUtc, int PendingCount, int ReviewCount,
    IReadOnlyList<AcbCallbackReceiptSummary> Receipts, IReadOnlyList<AcbCallbackHttpAttempt> HttpAttempts, DateTime MonitoringStartedAtUtc);
