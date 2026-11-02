using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontFulfillmentService
{
    // Cumulative whole-VND slicing conserves the original immutable pricing snapshot.
    internal static decimal SliceMoney(decimal total, decimal allBase, decimal startBase, decimal quantity)
    {
        Need(total >= 0 && total == decimal.Truncate(total) && allBase > 0 && startBase >= 0 && quantity >= 0 && startBase + quantity <= allBase, "Khoảng phân bổ tiền không hợp lệ.");
        var n = new BigInteger(total); var d = new BigInteger(allBase * 10000m);
        return (decimal)(n * new BigInteger((startBase + quantity) * 10000m) / d - n * new BigInteger(startBase * 10000m) / d);
    }
    public Task<StorefrontOperationAck> StartPreparationAsync(int id, StorefrontOperationRequest request, CancellationToken ct = default)
        => Command(id, request, "StartPreparation", StorefrontPermissions.Prepare, async (order, token) =>
        {
            Need(order.Phase == StorefrontOrderPhase.AwaitingPreparation && PaymentReady(order) && !await Incident(id, token), "Đơn chưa đủ điều kiện chuẩn bị hoặc đang cần đối soát.");
            Need(!await db.Set<StorefrontFulfillmentWork>().AnyAsync(x => x.StoreId == Store && x.StorefrontOrderId == id, token), "Đơn đã có hồ sơ chuẩn bị.");
            var sources = await Sources(id, token);
            var lines = await db.Set<StorefrontOrderLine>().Where(x => x.StoreId == Store && x.StorefrontOrderId == id).OrderBy(x => x.Id).ToListAsync(token);
            var holds = await db.InventoryReservations.AsNoTracking().Where(x => x.StoreId == Store && x.StorefrontOrderId == id &&
                x.ReferenceType == InventoryReferenceType.Storefront && x.Status == InventoryReservationStatus.Active).OrderBy(x => x.Id).ToListAsync(token);
            Need(sources.Count is > 0 and <= 100 && lines.Count is > 0 and <= 200 && holds.Count is > 0 and <= 20000, "Nguồn giữ hàng không đầy đủ hoặc vượt giới hạn.");
            Need(lines.Sum(x => x.NetAmount) == order.GoodsAmount && lines.All(line => holds.Where(x => x.StorefrontOrderLineId == line.Id).Sum(x => x.ReservedQty) == line.BaseQuantity), "Lượng giữ không khớp toàn bộ dòng hàng đã chốt.");
            using var original = JsonDocument.Parse(order.AddressSnapshotJson);
            Need(original.RootElement.TryGetProperty("allowNegative", out var negative) && negative.ValueKind is JsonValueKind.True or JsonValueKind.False, "Thiếu chính sách giữ hàng đã chốt.");
            var negatives = negative.GetBoolean();
            var variants = await db.ProductVariants.IgnoreQueryFilters().AsNoTracking().Include(x => x.Product).ThenInclude(x => x.BaseUnit)
                .Where(x => x.StoreId == Store && lines.Select(l => l.VariantId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, token);
            foreach (var source in sources)
            {
                Need(source.DeliveryOrderId is null && await db.Warehouses.AnyAsync(x => x.StoreId == Store && x.Id == source.SourceWarehouseId && x.LegalEntityId == source.SourceLegalEntityId && x.IsActive && x.LegalEntity.IsActive, token), "Kho/chủ thể nguồn không hoạt động; cần cửa hàng xử lý nguồn trước.");
                var childHolds = holds.Where(x => x.StorefrontFulfillmentSourceId == source.Id).ToList();
                Need(childHolds.Count > 0 && childHolds.All(x => x.WarehouseId == source.SourceWarehouseId && x.SourceLegalEntityId == source.SourceLegalEntityId && lines.Any(l => l.Id == x.StorefrontOrderLineId && l.VariantId == x.ProductVariantId)), "Giữ hàng nguồn không khớp đơn.");
                var child = new DeliveryOrder { StoreId = Store, Code = "WD" + id + "-" + source.Id, LookupToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                    SourceKind = DeliverySourceKind.Storefront, StorefrontOrderId = id, StorefrontFulfillmentSourceId = source.Id,
                    StorefrontAllowNegativeInventory = negatives, SourceWarehouseId = source.SourceWarehouseId, SourceLegalEntityId = source.SourceLegalEntityId,
                    CreatedByUserId = Actor, RecipientName = order.RecipientName, RecipientPhone = order.RecipientPhone, RecipientAddress = order.RecipientAddress,
                    Note = order.Note, State = DeliveryState.Picking };
                foreach (var hold in childHolds)
                {
                    var line = lines.Single(x => x.Id == hold.StorefrontOrderLineId);
                    var all = holds.Where(x => x.StorefrontOrderLineId == line.Id).OrderBy(x => x.StorefrontFulfillmentSourceId).ThenBy(x => x.Id).ToList();
                    var start = all.TakeWhile(x => x.Id != hold.Id).Sum(x => x.ReservedQty);
                    var discount = SliceMoney(line.Discount, line.BaseQuantity, start, hold.ReservedQty);
                    using var saved = JsonDocument.Parse(line.SnapshotJson);
                    var price = JsonSerializer.Deserialize<StorefrontCheckoutLine>(saved.RootElement.GetProperty("pricing").GetRawText(), Json)!;
                    using var role = JsonDocument.Parse(price.PromotionSnapshotJson);
                    if (role.RootElement.TryGetProperty("roleVersion", out var rv) && rv.GetInt32() == 1)
                    {
                        decimal Component(string name) => role.RootElement.GetProperty(name).GetDecimal();
                        discount = SliceMoney(Component("lineDiscount"), line.BaseQuantity, start, hold.ReservedQty) +
                            SliceMoney(Component("promotionDiscount"), line.BaseQuantity, start, hold.ReservedQty) +
                            SliceMoney(Component("headerDiscountAllocated"), line.BaseQuantity, start, hold.ReservedQty);
                    }
                    var net = SliceMoney(line.NetAmount, line.BaseQuantity, start, hold.ReservedQty);
                    var variant = variants[line.VariantId];
                    child.Lines.Add(new() { StoreId = Store, StorefrontOrderId = id, StorefrontOrderLineId = line.Id, StorefrontBaseQuantity = hold.ReservedQty,
                        VariantId = line.VariantId, ProductUnitConversionId = line.ProductUnitConversionId, SellingUnitId = line.SellingUnitId,
                        BaseUnitId = price.BaseUnitId ?? variant.Product.BaseUnitId, ItemName = line.ItemName, UnitName = line.UnitName, BaseUnitName = price.BaseUnitName ?? variant.Product.BaseUnit.Name,
                        OrderedQuantity = line.Quantity, BaseMultiplier = line.BaseMultiplier, UnitPrice = line.UnitPrice,
                        Gross = net + discount, LineDiscount = discount, Net = net });
                }
                child.QuotedTotal = child.Lines.Sum(x => x.Net); db.DeliveryOrders.Add(child); await db.SaveChangesAsync(token);
                source.DeliveryOrderId = child.Id;
                await db.SaveChangesAsync(token);
                db.Set<StorefrontFulfillmentWork>().Add(new() { StoreId = Store, StorefrontOrderId = id, FulfillmentSourceId = source.Id, DeliveryOrderId = child.Id });
                db.DeliveryPickingWorks.Add(new() { StoreId = Store, DeliveryOrderId = child.Id, PickerUserId = Actor, AssignedAtUtc = Now, StartedAtUtc = Now });
                foreach (var line in child.Lines) db.DeliveryPickingLines.Add(new() { StoreId = Store, DeliveryOrderId = child.Id, DeliveryOrderLineId = line.Id,
                    PlannedQuantity = line.StorefrontBaseQuantity!.Value, PlannedOriginalCoverage = 0 });
                await db.SaveChangesAsync(token);
            }
            order.Phase = StorefrontOrderPhase.Preparing; order.DeliveryState = StorefrontDeliveryState.Picking;
            Evidence(order, request, "PreparationStarted", new { sources = sources.Select(x => new { x.Id, x.DeliveryOrderId }) });
        }, ct);

    public Task<StorefrontOperationAck> RecordPickingAsync(int id, StorefrontPickingRequest request, CancellationToken ct = default)
        => Command(id, request, "RecordPicking", StorefrontPermissions.Prepare, async (order, token) =>
        {
            Need(order.Phase == StorefrontOrderPhase.Preparing && PaymentReady(order) && !await Incident(id, token), "Không thể cập nhật soạn hàng ở trạng thái hiện tại.");
            var works = await Works(id, token); var childIds = works.Select(x => x.DeliveryOrderId).ToList();
            var rows = await db.DeliveryPickingLines.Include(x => x.Quote).Where(x => x.StoreId == Store && childIds.Contains(x.DeliveryOrderId) && x.IsActive).ToListAsync(token);
            if (request.Lines is null || request.Lines.Count != rows.Count || request.Lines.Select(x => (x.FulfillmentSourceId, x.OrderLineId)).Distinct().Count() != rows.Count)
                throw new ValidationAppException("Cần ghi đủ từng dòng nguồn, không trùng.");
            foreach (var row in rows)
            {
                var work = works.Single(x => x.DeliveryOrderId == row.DeliveryOrderId);
                var input = request.Lines.SingleOrDefault(x => x.FulfillmentSourceId == work.FulfillmentSourceId && x.OrderLineId == row.Quote.StorefrontOrderLineId)
                    ?? throw new ValidationAppException("Dòng soạn không thuộc nguồn của đơn.");
                var qty = Quantity(input.BaseQuantity); Need(qty <= row.Quote.StorefrontBaseQuantity, "Không soạn vượt lượng nguồn đã chốt; điều chỉnh đơn cần quy trình riêng.");
                row.ReportedQuantity = qty; row.ReportedByUser(Actor, Now); row.ApprovedQuantity = qty == row.Quote.StorefrontBaseQuantity ? qty : null;
                row.ReportFactKind = "picker-report";
                row.ShortageReason = qty < row.Quote.StorefrontBaseQuantity ? request.Note.Trim() : null;
                Need(qty == row.Quote.StorefrontBaseQuantity || !string.IsNullOrWhiteSpace(request.Note), "Ghi rõ lý do thiếu hàng.");
                row.ApprovedOriginalCoverage = qty == row.Quote.StorefrontBaseQuantity ? 0 : null; row.ApprovedNet = qty == row.Quote.StorefrontBaseQuantity ? row.Quote.Net : null;
            }
            await db.SaveChangesAsync(token);
            foreach (var work in works)
            {
                var complete = rows.Where(x => x.DeliveryOrderId == work.DeliveryOrderId).All(x => x.ReportedQuantity == x.Quote.StorefrontBaseQuantity);
                work.Stage = complete ? StorefrontFulfillmentStage.Picked : StorefrontFulfillmentStage.Picking;
                var child = await db.DeliveryOrders.SingleAsync(x => x.StoreId == Store && x.Id == work.DeliveryOrderId, token);
                child.State = complete ? DeliveryState.ReadyForHandover : DeliveryState.Picking;
                var picking = await db.DeliveryPickingWorks.SingleAsync(x => x.StoreId == Store && x.DeliveryOrderId == child.Id, token);
                picking.SubmittedAtUtc = Now; picking.ApprovedRevision = complete ? child.Revision : null;
                picking.ApprovedByUserId = complete ? Actor : null; picking.ApprovedAtUtc = complete ? Now : null;
                picking.ApprovedTotal = complete ? child.QuotedTotal : null;
                work.LatestEvidence = Evidence(order, request, complete ? "Picked" : "PickingShortage", request.Lines.Where(x => x.FulfillmentSourceId == work.FulfillmentSourceId), work.FulfillmentSourceId);
            }
            Evidence(order, request, "PickingRecorded", request.Lines);
        }, ct);
}

internal static class StorefrontPickingReporting
{
    internal static void ReportedByUser(this DeliveryPickingLine row, int actor, DateTime time)
    { row.ReporterUserId = actor; row.ReportedAtUtc = time; }
}
