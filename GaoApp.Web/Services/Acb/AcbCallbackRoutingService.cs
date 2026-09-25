using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.Acb;

public sealed class AcbCallbackRouteForm
{
    [Required, MaxLength(253)] public string Host { get; set; } = "";
    [Range(1, int.MaxValue)] public int? TargetStoreId { get; set; }
    [MaxLength(256)] public string? Version { get; set; }
}
public sealed record AcbCallbackStoreChoice(int Id, string Name, string Subdomain);
public sealed record AcbCallbackRouteRow(string Host, int? TargetStoreId, string Version);
public sealed record AcbCallbackRouteHistory(string Host, int? PreviousStoreId, int? TargetStoreId, int ActorUserId, DateTime ChangedAtUtc);
public sealed record AcbCallbackRoutingPage(IReadOnlyList<AcbCallbackStoreChoice> Stores,
    IReadOnlyList<AcbCallbackRouteRow> Routes, IReadOnlyList<AcbCallbackRouteHistory> History);

public sealed class AcbCallbackRoutingService(AppDbContext db, IOptions<AcbCallbackRoutingOptions> options)
{
    public async Task<bool> CanManageAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.Identity?.IsAuthenticated != true || !int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id <= 0)
            return false;
        // Store-level integration permission alone must never allow rerouting another store's bank notifications.
        return await db.Users.AsNoTracking().AnyAsync(x => x.Id == id && x.IsActive && !x.IsDeleted && x.IsHostAdmin, ct);
    }

    public async Task<AcbCallbackRoutingPage> PageAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (!await CanManageAsync(user, ct)) throw new UnauthorizedAccessException();
        var stores = await db.Stores.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Name).Select(x => new AcbCallbackStoreChoice(x.Id, x.Name, x.SubDomain)).ToListAsync(ct);
        var routes = await db.Set<AcbCallbackRoute>().AsNoTracking().ToListAsync(ct);
        var rows = options.Value.Hosts.Select(x => x.Trim().ToLowerInvariant()).Select(host =>
        {
            var route = routes.SingleOrDefault(x => x.Host == host);
            return new AcbCallbackRouteRow(host, route?.TargetStoreId, Convert.ToBase64String(route?.RowVersion ?? []));
        }).ToList();
        var history = await db.Set<AcbCallbackRouteChange>().AsNoTracking().OrderByDescending(x => x.Id).Take(30)
            .Select(x => new AcbCallbackRouteHistory(x.Route.Host, x.PreviousStoreId, x.TargetStoreId, x.ActorUserId, x.ChangedAtUtc)).ToListAsync(ct);
        return new(stores, rows, history);
    }

    public async Task SaveAsync(ClaimsPrincipal user, AcbCallbackRouteForm form, CancellationToken ct)
    {
        if (!await CanManageAsync(user, ct)) throw new UnauthorizedAccessException();
        var host = form.Host?.Trim().ToLowerInvariant() ?? "";
        if (!options.Value.Handles(host)) throw new InvalidOperationException("URL callback này chưa nằm trong cấu hình máy chủ.");
        if (form.TargetStoreId.HasValue)
        {
            if (!await db.Stores.AnyAsync(x => x.Id == form.TargetStoreId && x.IsActive, ct))
                throw new InvalidOperationException("Hãy chọn cửa hàng đang hoạt động.");
            // This is a host-admin operation; explicitly constrain the cross-store settings read to the selected store.
            if (!await db.Set<StoreAcbSettings>().IgnoreQueryFilters().AnyAsync(x => !x.IsDeleted && x.StoreId == form.TargetStoreId && x.CallbackApiKeyProtected != "", ct))
                throw new InvalidOperationException("Cửa hàng được chọn chưa lưu x-api-key trong Cài đặt thanh toán ACB.");
        }
        var route = await db.Set<AcbCallbackRoute>().SingleOrDefaultAsync(x => x.Host == host, ct);
        var version = Convert.ToBase64String(route?.RowVersion ?? []);
        if (!string.Equals(form.Version ?? "", version, StringComparison.Ordinal))
            throw new InvalidOperationException("Cấu hình đã được thay đổi ở phiên khác. Hãy tải lại trang trước khi lưu.");
        if (route != null && route.TargetStoreId == form.TargetStoreId) return;
        var lastAssignedStoreId = route?.TargetStoreId;
        if (route != null && lastAssignedStoreId == null)
            lastAssignedStoreId = await db.Set<AcbCallbackRouteChange>().Where(x => x.RouteId == route.Id && x.TargetStoreId.HasValue)
                .OrderByDescending(x => x.Id).Select(x => x.TargetStoreId).FirstOrDefaultAsync(ct);
        if (lastAssignedStoreId is int previous && form.TargetStoreId.HasValue && previous != form.TargetStoreId)
        {
            var outstanding = await db.Set<AcbQrSession>().IgnoreQueryFilters().AnyAsync(x => !x.IsDeleted && x.StoreId == previous &&
                x.Status != AcbSessionStatus.Completed && x.Status != AcbSessionStatus.Cancelled, ct);
            var queued = await db.Set<AcbCallbackReceipt>().IgnoreQueryFilters().AnyAsync(x => !x.IsDeleted && x.StoreId == previous && x.ProcessedAtUtc == null, ct);
            if (outstanding || queued) throw new InvalidOperationException("Cửa hàng đang nhận còn QR hoặc callback chưa xử lý xong. Hãy xử lý trước khi chuyển URL sang cửa hàng khác.");
        }
        var oldStoreId = route?.TargetStoreId;
        if (route == null) { route = new AcbCallbackRoute { Host = host }; db.Add(route); }
        route.TargetStoreId = form.TargetStoreId;
        db.Add(new AcbCallbackRouteChange { Route = route, PreviousStoreId = oldStoreId, TargetStoreId = form.TargetStoreId,
            ActorUserId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), ChangedAtUtc = DateTime.UtcNow });
        // One SaveChanges commits routing and its history together; rowversion/unique host prevent lost updates.
        await db.SaveChangesAsync(ct);
    }

    public async Task<string[]> UrlsForStoreAsync(int storeId, CancellationToken ct)
    {
        var hosts = await db.Set<AcbCallbackRoute>().AsNoTracking().Where(x => x.TargetStoreId == storeId).Select(x => x.Host).ToListAsync(ct);
        return hosts.Where(options.Value.Handles).Select(x => $"https://{x}/Admin/api-callback").ToArray();
    }
}
