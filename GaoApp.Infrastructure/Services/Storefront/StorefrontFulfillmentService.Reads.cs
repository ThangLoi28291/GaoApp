using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontFulfillmentService
{
    private IQueryable<StorefrontOrder> ShortageOrders() => db.StorefrontOrders.FromSqlInterpolated($@"
SELECT o.* FROM [StorefrontOrders] o WHERE o.[StoreId]={Store} AND o.[IsDeleted]=0 AND (
 EXISTS(SELECT 1 FROM [DeliveryPickingLines] p JOIN [DeliveryOrderLines] l ON l.StoreId=p.StoreId AND l.Id=p.DeliveryOrderLineId
 WHERE l.StoreId=o.StoreId AND l.StorefrontOrderId=o.Id AND p.IsDeleted=0 AND p.IsActive=1 AND p.ReportedQuantity<l.StorefrontBaseQuantity)
 OR (NOT EXISTS(SELECT 1 FROM [StorefrontFulfillmentWorks] w WHERE w.StoreId=o.StoreId AND w.StorefrontOrderId=o.Id)
 AND JSON_VALUE(o.AddressSnapshotJson,'$.hasInventoryShortage')='true')
 OR EXISTS(SELECT 1 FROM [InventoryReservations] r JOIN [InventoryBalances] b ON b.StoreId=r.StoreId AND b.WarehouseId=r.WarehouseId AND b.ProductVariantId=r.ProductVariantId
 WHERE r.StoreId=o.StoreId AND r.StorefrontOrderId=o.Id AND r.IsDeleted=0 AND r.Status=1 AND r.ReferenceType=12 AND b.IsDeleted=0 AND b.OnHandQty<b.ReservedQty))").AsNoTracking();
    public async Task<StorefrontFulfillmentQueue> ListAsync(StorefrontFulfillmentQuery query, CancellationToken ct = default)
    {
        await Guard(StorefrontPermissions.ViewOrders, ct);
        if (query.Page < 1 || query.Page > 1000000 || query.PageSize is < 1 or > 100 || query.Search?.Length > 100 ||
            query.Phase.HasValue && !Enum.IsDefined(query.Phase.Value) || query.DeliveryMethod.HasValue && !Enum.IsDefined(query.DeliveryMethod.Value) ||
            query.PaymentMethod.HasValue && !Enum.IsDefined(query.PaymentMethod.Value)) throw new ValidationAppException("Bộ lọc hoặc phân trang không hợp lệ.");
        var q = db.StorefrontOrders.AsNoTracking().Where(x => x.StoreId == Store);
        if (query.Phase.HasValue) q = q.Where(x => x.Phase == query.Phase);
        if (query.DeliveryMethod.HasValue) q = q.Where(x => x.DeliveryMethod == query.DeliveryMethod);
        if (query.PaymentMethod.HasValue) q = q.Where(x => x.PaymentMethod == query.PaymentMethod);
        if (!string.IsNullOrWhiteSpace(query.Search)) { var term = query.Search.Trim(); q = q.Where(x => x.Code.Contains(term) || x.RecipientName.Contains(term) || x.RecipientPhone.Contains(term)); }
        var shortages = ShortageOrders().Select(x => x.Id);
        if (query.ShortageOnly) q = q.Where(x => shortages.Contains(x.Id));
        var total = await q.CountAsync(ct); var rows = await q.OrderByDescending(x => x.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var pageIds = rows.Select(x => x.Id).ToList(); var missing = await shortages.Where(x => pageIds.Contains(x)).ToListAsync(ct);
        return new(query.Page, query.PageSize, total, rows.Select(x => new StorefrontOrderInboxItem(x.Id, x.Code, StorefrontCheckoutService.AsUtc(x.CreatedAtUtc),
            x.RecipientName, x.Phase, x.TotalDue, missing.Contains(x.Id), x.PaymentMethod, x.PaymentState, x.DeliveryMethod)).ToList());
    }
    public async Task<StorefrontFulfillmentDetail> GetAsync(int id, CancellationToken ct = default)
    {
        await Guard(StorefrontPermissions.ViewOrders, ct);
        var order = await db.StorefrontOrders.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == id, ct) ?? throw new NotFoundAppException("Không tìm thấy đơn website.");
        var sources = await db.Set<StorefrontFulfillmentSource>().AsNoTracking().Where(x => x.StoreId == Store && x.StorefrontOrderId == id).OrderBy(x => x.Id).ToListAsync(ct);
        var work = await db.Set<StorefrontFulfillmentWork>().AsNoTracking().Where(x => x.StoreId == Store && x.StorefrontOrderId == id).ToListAsync(ct);
        var children = await db.DeliveryOrders.AsNoTracking().Include(x => x.Lines).Where(x => x.StoreId == Store && x.StorefrontOrderId == id).ToListAsync(ct);
        var childIds = children.Select(x => x.Id).ToList();
        var picks = await db.DeliveryPickingLines.AsNoTracking().Where(x => x.StoreId == Store && childIds.Contains(x.DeliveryOrderId)).ToListAsync(ct);
        var stock = await db.DeliveryJournalEntries.AsNoTracking().Where(x => x.StoreId == Store && childIds.Contains(x.DeliveryOrderId) && (x.Kind == DeliveryJournalKind.Dispatch || x.Kind == DeliveryJournalKind.Return)).ToListAsync(ct);
        var whIds = sources.Select(x => x.SourceWarehouseId).ToList(); var ownerIds = sources.Select(x => x.SourceLegalEntityId).ToList();
        var wh = await db.Warehouses.AsNoTracking().Where(x => x.StoreId == Store && whIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var owners = await db.LegalEntities.AsNoTracking().Where(x => x.StoreId == Store && ownerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var result = new List<StorefrontFulfillmentChild>();
        foreach (var w in work)
        {
            var source = sources.Single(x => x.Id == w.FulfillmentSourceId); var child = children.Single(x => x.Id == w.DeliveryOrderId);
            result.Add(new(source.Id, child.Id, wh[source.SourceWarehouseId], owners[source.SourceLegalEntityId], w.Stage, w.CourierUserId, w.CarrierName, w.TrackingCode,
                child.Lines.OrderBy(x => x.Id).Select(line => {
                    var picked = picks.SingleOrDefault(x => x.DeliveryOrderLineId == line.Id)?.ReportedQuantity ?? 0;
                    return new StorefrontFulfillmentLine(line.StorefrontOrderLineId!.Value, line.Id, line.ItemName, line.BaseUnitName, Qty(line.StorefrontBaseQuantity!.Value), Qty(picked),
                        Qty(stock.Where(x => x.DeliveryOrderLineId == line.Id && x.Kind == DeliveryJournalKind.Dispatch).Sum(x => x.BaseQuantity)),
                        Qty(stock.Where(x => x.DeliveryOrderLineId == line.Id && x.Kind == DeliveryJournalKind.Return).Sum(x => x.BaseQuantity)), Qty(Math.Max(0, line.StorefrontBaseQuantity.Value - picked)));
                }).ToList()));
        }
        var evidence = await db.Set<StorefrontFulfillmentEvidence>().AsNoTracking().Where(x => x.StoreId == Store && x.StorefrontOrderId == id).OrderByDescending(x => x.Id).Take(200).ToListAsync(ct);
        var users = evidence.Select(x => x.ActorUserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(x => users.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName ?? x.UserName, ct);
        var incident = await Incident(id, ct);
        return new(id, order.Code, order.Revision, Convert.ToBase64String(order.RowVersion), order.Phase, order.PaymentState, order.DeliveryMethod,
            order.RecipientName, order.RecipientPhone, order.RecipientAddress, incident,
            incident ? "Sự cố hàng thực tế chưa đối soát. Dừng bàn giao/ghi bán." : order.PaymentState == StorefrontPaymentState.Review ? "Khoản tiền cần đối soát; chưa được ghi bán." : null,
            result, evidence.Select(x => new StorefrontOperationHistory(StorefrontCheckoutService.AsUtc(x.OccurredAtUtc), x.Kind, names.GetValueOrDefault(x.ActorUserId, "Nhân viên"), x.Note)).ToList());
    }
}
