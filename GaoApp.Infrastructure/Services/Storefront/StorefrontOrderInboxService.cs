using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed class StorefrontOrderInboxService(AppDbContext db, ICurrentUser user, ICurrentStorePermissionService permissions) : IStorefrontOrderInboxService
{
    private int Store => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new ForbiddenAppException("Chưa xác định cửa hàng.");
    private async Task Guard(CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.UserId is not > 0 || !await permissions.HasPermissionAsync(Store, user.UserId.Value, StorefrontPermissions.ViewOrders, ct) ||
            !await db.Stores.AnyAsync(x => x.Id == Store && x.IsActive, ct)) throw new ForbiddenAppException("Không có quyền xem đơn website của cửa hàng này.");
    }
    private IQueryable<StorefrontOrder> Orders() => db.StorefrontOrders.AsNoTracking().Where(x => x.StoreId == Store &&
        db.Set<StorefrontEvent>().Any(e => e.StoreId == Store && e.StorefrontOrderId == x.Id && e.Kind == "OrderCreated"));
    public async Task<StorefrontOrderInboxPage> ListAsync(int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        await Guard(ct); StorefrontProductValidation.Page(page, pageSize);
        var query = Orders(); var total = await query.CountAsync(ct);
        var awaiting = await query.CountAsync(x => x.Phase == StorefrontOrderPhase.AwaitingPreparation, ct);
        var newest = await query.Select(x => (int?)x.Id).MaxAsync(ct) ?? 0;
        var rows = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(page, pageSize, total, rows.Select(x => new StorefrontOrderInboxItem(x.Id, x.Code, StorefrontCheckoutService.AsUtc(x.CreatedAtUtc), x.RecipientName,
            x.Phase, x.TotalDue, HasShortage(x.AddressSnapshotJson),x.PaymentMethod,x.PaymentState)).ToList(), awaiting, newest);
    }
    public async Task<StorefrontGuestOrder> GetAsync(int id, CancellationToken ct = default)
    {
        await Guard(ct); var order = await Orders().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundAppException("Không tìm thấy đơn website.");
        return await StorefrontCheckoutService.ProjectOrder(db, order, ct);
    }
    public async Task<StorefrontInboxNotification> NotificationsAsync(int afterId, CancellationToken ct = default)
    {
        await Guard(ct); if (afterId < 0) throw new ValidationAppException("Mốc đơn mới không hợp lệ.");
        var query = Orders(); return new(await query.CountAsync(x => x.Id > afterId, ct),
            await query.CountAsync(x => x.Phase == StorefrontOrderPhase.AwaitingPreparation, ct), await query.Select(x => (int?)x.Id).MaxAsync(ct) ?? 0);
    }
    private static bool HasShortage(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("hasInventoryShortage", out var value) && value.ValueKind == JsonValueKind.True;
    }
}
