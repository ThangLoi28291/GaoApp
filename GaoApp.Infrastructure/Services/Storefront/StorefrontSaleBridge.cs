using System.Globalization;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Services.Invoices;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed class StorefrontSaleBridge(AppDbContext db, ICurrentUser user, ICurrentPOSContext pos,
    ICurrentStorePermissionService permissions, IOrderNumberGenerator numbers, IEnumerable<IOrderFinalizeGuard> finalizeGuards,
    TimeProvider clock, GaoApp.Application.InvoiceNotifications.IInvoiceNotificationCapture? notifications = null) : IStorefrontSaleBridge
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static void Need(bool value, string message) { if (!value) throw new ConflictAppException(message); }
    private sealed record PaymentSource(decimal Amount, int? GlobalId, int? CodId, int? WithholdingId, PaymentMethod Method, DateTime AtUtc, string Reference);
    private sealed record FundingSlice(PaymentSource Source, decimal GoodsAmount, decimal ServiceAmount);
    private static List<FundingSlice> FundingPlan(Order order, StorefrontOrder parent, StorefrontServiceRevenue? fee,
        IReadOnlyList<PaymentSource> sources, IReadOnlyDictionary<int, int> withholdingOwners)
    {
        var goods = parent.GoodsAmount; var service = parent.ShippingCharge;
        var ownerGoods = order.LegalEntityAllocations.GroupBy(x => x.LegalEntityId).ToDictionary(x => x.Key, x => x.Sum(a => a.NetAmount));
        var plan = new List<FundingSlice>();
        foreach (var source in sources.OrderByDescending(x => x.WithholdingId.HasValue))
        {
            decimal amount;
            if (source.WithholdingId is int id)
            {
                var owner = withholdingOwners[id]; amount = Math.Min(ownerGoods.GetValueOrDefault(owner), source.Amount);
                ownerGoods[owner] = ownerGoods.GetValueOrDefault(owner) - amount;
                Need(source.Amount == amount || fee is not null && fee.LegalEntityId == owner && source.Amount - amount <= service,
                    "Khoản hãng giữ lại vượt ngân sách của chính chủ thể chi phí; không bù sang chủ thể khác.");
            }
            else amount = Math.Min(goods, source.Amount);
            goods -= amount; service -= source.Amount - amount;
            Need(goods >= 0 && service >= 0, "Nguồn quyết toán vượt tiền hàng/phí còn lại.");
            plan.Add(new(source, amount, source.Amount - amount));
        }
        Need(goods == 0 && service == 0, "Chưa đủ nguồn quyết toán tiền hàng/phí."); return plan;
    }
    public async Task<int> PostAsync(int storefrontOrderId, Guid requestId, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Storefront sale requires its locked parent transaction.");
        var store = db.CurrentStoreId ?? throw new ForbiddenAppException("Cần chọn cửa hàng.");
        Need(requestId != Guid.Empty && user.IsAuthenticated && user.UserId is > 0 && pos.IsAvailable && pos.StoreId == store && pos.UserId == user.UserId && pos.TerminalId > 0 &&
            await permissions.HasPermissionAsync(store, user.UserId!.Value, StorefrontPermissions.PostSale, ct), "Ghi bán cần nhân viên có quyền tại quầy thật của cửa hàng.");
        var actor = user.UserId!.Value; var now = clock.GetUtcNow().UtcDateTime;
        var shift = await db.POSShifts.FromSqlInterpolated($"SELECT * FROM [POSShifts] WITH(UPDLOCK,HOLDLOCK) WHERE [StoreId]={store} AND [TerminalId]={pos.TerminalId} AND [Status]={(int)POSShiftStatus.Open}")
            .SingleOrDefaultAsync(x => x.OpenedByUserId == actor, ct);
        Need(shift is not null && await db.POSTerminals.AnyAsync(x => x.StoreId == store && x.Id == pos.TerminalId && x.IsActive, ct), "Mở ca thật do chính bạn sở hữu trước khi ghi bán.");
        var parent = await db.StorefrontOrders.SingleAsync(x => x.StoreId == store && x.Id == storefrontOrderId, ct);
        Need(parent.Phase == StorefrontOrderPhase.Delivered && parent.PaymentState == StorefrontPaymentState.Confirmed && parent.RefundState == StorefrontRefundState.None &&
            !await db.Set<StorefrontCustodyIncident>().AnyAsync(x => x.StoreId == store && x.StorefrontOrderId == parent.Id && x.ReconciledByEvidenceId == null, ct), "Đơn chưa giao/đối soát hoàn tất hoặc còn sự cố/hoàn tiền.");
        var cancellation = await StorefrontCancellationFacts.ReadAsync(db, store, parent.Id, ct);
        Need(cancellation.PendingRequest is null && !cancellation.ReturnAuthorised && !cancellation.NeedsDeliveryReview,
            "Còn yêu cầu hủy, thu hồi hoặc xung đột giao / hủy chưa được xử lý; không ghi bán.");
        Need(!await db.Set<StorefrontSaleBinding>().AnyAsync(x => x.StoreId == store && x.StorefrontOrderId == parent.Id, ct), "Đơn website đã ghi bán.");
        var works = await db.Set<StorefrontFulfillmentWork>().AsNoTracking().Where(x => x.StoreId == store && x.StorefrontOrderId == parent.Id).ToListAsync(ct);
        var sources = await db.Set<StorefrontFulfillmentSource>().AsNoTracking().Where(x => x.StoreId == store && x.StorefrontOrderId == parent.Id).ToListAsync(ct);
        Need(works.Count > 0 && works.Count == sources.Count && works.All(x => x.Stage == StorefrontFulfillmentStage.Delivered), "Chưa có chứng cứ mọi nguồn giao thành công.");
        var webLines = await db.Set<StorefrontOrderLine>().AsNoTracking().Where(x => x.StoreId == store && x.StorefrontOrderId == parent.Id).OrderBy(x => x.Id).ToListAsync(ct);
        var currentInvoiceFlags = await db.ProductVariants.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == store && webLines.Select(l => l.VariantId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => !x.IsDeleted && x.HasInputInvoice, ct);
        bool Eligible(StorefrontOrderLine line) => line.HasInputInvoiceSnapshot ?? currentInvoiceFlags.GetValueOrDefault(line.VariantId);
        var children = await db.DeliveryOrders.AsNoTracking().Include(x => x.Lines).Where(x => x.StoreId == store && x.StorefrontOrderId == parent.Id).ToListAsync(ct);
        var costs = new Dictionary<int, DeliverySaleLinePlan>(); var evidence = new DeliverySaleCostEvidence(db);
        foreach (var child in children.OrderBy(x => x.Id)) await DeliverySqlLock.AcquireAsync(db, "order:" + store + ":" + child.Id, ct);
        await new GaoApp.Infrastructure.Repositories.Inventory.InventoryBalanceRepository(db).LockBatchAsync(children.SelectMany(c => c.Lines.Select(l =>
            new GaoApp.Application.DTOs.Inventory.InventoryPostingLockKey(store, c.SourceWarehouseId, l.VariantId))).Distinct().ToList(), ct);
        foreach (var child in children.OrderBy(x => x.Id))
        {
            var plans = child.Lines.Select(x => new DeliverySaleLinePlan(x.Id, x.StorefrontOrderLineId!.Value, x.VariantId,
                x.StorefrontBaseQuantity!.Value, x.Gross, x.LineDiscount + x.AllocatedOrderDiscount, x.Net, 0, "", [])).ToList();
            foreach (var proof in await evidence.ReadDeliveredSlicesAsync(child.Id, plans, ct)) costs.Add(proof.DeliveryLineId, proof);
        }
        Need(children.Count == sources.Count && children.Sum(x => x.QuotedTotal) == parent.GoodsAmount && webLines.Sum(x => x.NetAmount) == parent.GoodsAmount &&
            costs.Values.Sum(x => x.DeliveredBaseQuantity) == webLines.Sum(x => x.BaseQuantity), "Nguồn xuất/tiền đã chốt không cân với đơn hàng.");
        var valuation = await StorefrontValuationEvidence.ReadAsync(db, costs.Values.SelectMany(x => x.Slices).Select(x => x.InventoryTransactionId), ct);
        foreach (var id in costs.Keys.ToList())
        {
            var txIds = costs[id].Slices.Select(x => x.InventoryTransactionId).ToHashSet();
            var interpreted = valuation.Roots.Where(x => txIds.Contains(x.InventoryTransactionId)).Select(x => valuation.Evaluate(x)).ToList();
            Need(interpreted.Count > 0 && interpreted.All(x => x.State != GaoApp.Application.Services.Inventory.SaleValuationCostPolicy.Quality.DataIntegrityConflict), "Nguồn điều chỉnh giá vốn không khớp; chưa ghi bán.");
            costs[id] = costs[id] with { RecordedCost = interpreted.All(x => x.Cost.HasValue) ? interpreted.Sum(x => x.Cost!.Value) : costs[id].RecordedCost,
                CostQuality = interpreted.All(x => x.State == GaoApp.Application.Services.Inventory.SaleValuationCostPolicy.Quality.Finalized) ? "Confirmed" : interpreted.Any(x => !x.Cost.HasValue) ? "Unknown" : "Provisional" };
        }
        var payments = await PaymentSources(parent, ct); Need(payments.Sum(x => x.Amount) == parent.TotalDue, "Ngân sách tiền thực nhận/giữ lại không cân toàn đơn.");
        StorefrontServiceRevenue? fee = null;
        if (parent.ShippingCharge > 0)
        {
            Need(parent.ShippingFeeTreatment == StorefrontShippingFeeTreatment.StoreServiceFee, "Phí chưa có cách ghi nhận dịch vụ được hỗ trợ; chưa ghi bán.");
            var proof = await StorefrontServiceSource.ReadAsync(db, parent, ct);
            var amounts = InvoiceNativeServiceAmounts.Inclusive(parent.ShippingCharge, proof.TaxRate);
            fee = new() { StoreId = store, StorefrontOrderId = parent.Id, LegalEntityId = proof.LegalEntityId, TaxId = proof.TaxId, Name = proof.Name,
                GrossAmount = parent.ShippingCharge, NetAmount = amounts.Net, TaxAmount = amounts.Tax, TaxRate = proof.TaxRate,
                InvoiceRoute = proof.InvoiceRoute, SettlingShiftId = shift!.Id, ActorUserId = actor, RecognizedAtUtc = now, SourceSnapshotJson = JsonSerializer.Serialize(proof, Json) };
            await RequireInvoiceOwner(store, fee.LegalEntityId, ct);
        }
        foreach (var source in sources)
            if (children.Where(x => x.StorefrontFulfillmentSourceId == source.Id).SelectMany(x => x.Lines).Any(l => webLines.Any(w => w.Id == l.StorefrontOrderLineId && Eligible(w))))
                await RequireInvoiceOwner(store, source.SourceLegalEntityId, ct);
        Need(parent.UseMultiLegalEntitySnapshot.HasValue && parent.LegalEntityModeCapturedAtUtc.HasValue, "Đơn cũ thiếu bằng chứng chế độ nguồn pháp lý đã chốt; cần đối soát trước, không tự đổi chế độ.");
        using var originalCohort = JsonDocument.Parse(parent.AddressSnapshotJson); DateTime? activation = null;
        Need(originalCohort.RootElement.TryGetProperty("sources", out var savedSources) && savedSources.ValueKind == JsonValueKind.Array, "Thiếu thứ tự và tập nguồn đã chốt của đơn.");
        var originalPriorities = new Dictionary<int, int>();
        foreach (var source in sources)
        {
            var matches = savedSources.EnumerateArray().Where(x => x.GetProperty("warehouseId").GetInt32() == source.SourceWarehouseId && x.GetProperty("legalEntityId").GetInt32() == source.SourceLegalEntityId)
                .Select(x => x.GetProperty("priority").GetInt32()).ToList();
            Need(matches.Count == 1 && matches[0] > 0, "Nguồn đã chốt thiếu thứ tự pháp lý gốc; không dùng thứ tự mới để đổi nguồn đơn cũ.");
            originalPriorities.Add(source.Id, matches[0]);
        }
        if (parent.UseMultiLegalEntitySnapshot == true)
        {
            Need(originalCohort.RootElement.TryGetProperty("legalEntityActivationAtUtcSnapshot", out var captured) && captured.TryGetDateTime(out var value) && value <= parent.LegalEntityModeCapturedAtUtc,
                "Đơn thiếu thời điểm kích hoạt đa chủ thể gốc; không tự dùng cấu hình mới để hợp thức hóa nguồn cũ.");
            activation = StorefrontCheckoutService.AsUtc(captured.GetDateTime());
        }
        var order = new Order { StoreId = store, POSShiftId = shift!.Id, Note = "Đơn website " + parent.Code + "; đã xuất theo nguồn giao, không xuất lần hai.",
            GrandTotal = parent.GoodsAmount, PaidTotal = parent.GoodsAmount, BalanceDue = 0, UseMultiLegalEntity = parent.UseMultiLegalEntitySnapshot!.Value,
            LegalEntityModeCapturedAtUtc = parent.LegalEntityModeCapturedAtUtc, LegalEntityAllocatedAtUtc = now, LegalEntityCount = sources.Select(x => x.SourceLegalEntityId).Distinct().Count(),
            LegalEntityActivationAtUtcSnapshot = activation,
            InvoiceIssuanceRoute = InvoiceIssuanceRoute.Automatic, InvoiceIssuanceRouteSelectedAtUtc = now, InvoiceIssuanceRouteSelectedByUserId = actor,
            CustomerPriceTierSnapshot = "RETAIL" };
        var mapped = new Dictionary<int, OrderLine>(); var pricing = new Dictionary<int, StorefrontCheckoutLine>();
        foreach (var web in webLines)
        {
            using var doc = JsonDocument.Parse(web.SnapshotJson); var price = JsonSerializer.Deserialize<StorefrontCheckoutLine>(doc.RootElement.GetProperty("pricing").GetRawText(), Json)!;
            pricing[web.Id] = price;
            var fragments = children.SelectMany(x => x.Lines).Where(x => x.StorefrontOrderLineId == web.Id).ToList();
            Need(fragments.Sum(x => x.StorefrontBaseQuantity) == web.BaseQuantity && fragments.Sum(x => x.Net) == web.NetAmount, "Dòng bán không đủ đúng nguồn vật lý.");
            var cost = fragments.Sum(x => costs[x.Id].RecordedCost); var provisional = fragments.Any(x => costs[x.Id].CostQuality != "Confirmed");
            var first = fragments.First();
            var line = new OrderLine { StoreId = store, StorefrontOrderId = parent.Id, StorefrontOrderLineId = web.Id,
                StorefrontRoundingAdjustment = web.RoundingAdjustment, ProductId = web.ProductId, VariantId = web.VariantId,
                ItemName = web.ItemName, Quantity = web.Quantity, Multiplier = web.BaseMultiplier, BaseQuantity = web.BaseQuantity,
                ProductUnitConversionId = web.ProductUnitConversionId, SellingUnitId = web.SellingUnitId, SellingUnitName = web.UnitName, UnitName = web.UnitName,
                BaseUnitId = first.BaseUnitId, BaseUnitName = first.BaseUnitName, UnitPrice = web.UnitPrice, OriginalUnitPrice = web.UnitPrice,
                LineDiscount = web.Discount, LineTotal = web.NetAmount, HasInputInvoiceSnapshot = Eligible(web),
                IsPromotionGift = web.IsGift, GiftPromotionName = web.IsGift ? price.PromotionName : null, PromotionName = web.IsGift ? null : price.PromotionName,
                LineCostTotal = decimal.Round(cost, 2, MidpointRounding.AwayFromZero), UnitCostSnapshot = decimal.Round(cost / web.BaseQuantity, 2, MidpointRounding.AwayFromZero),
                GrossProfit = web.NetAmount - decimal.Round(cost, 2, MidpointRounding.AwayFromZero), IsProvisionalCost = provisional,
                ProvisionalUnitCost = provisional ? decimal.Round(cost / web.BaseQuantity, 2, MidpointRounding.AwayFromZero) : null,
                CostSnapshotNote = "storefront-dispatch-v1:" + JsonSerializer.Serialize(fragments.Select(x => new { x.Id, slices = costs[x.Id].Slices }), Json) };
            using var promotion = JsonDocument.Parse(price.PromotionSnapshotJson); var meta = promotion.RootElement;
            if (meta.TryGetProperty("roleVersion", out var roleVersion) && roleVersion.GetInt32() == 1)
            {
                int? Id(string name) => meta.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
                decimal Money(string name) => meta.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : 0;
                line.PromotionId = Id("promotionId"); line.PromotionType = Id("promotionType") is int t ? (PromotionType)t : null;
                line.ComboPromotionId = Id("comboPromotionId"); line.GiftPromotionId = Id("giftPromotionId");
                line.LineDiscount = Money("lineDiscount"); line.PromotionDiscount = Money("promotionDiscount"); line.ComboAllocatedDiscount = Money("comboAllocatedDiscount");
                line.LineTotal = web.NetAmount + Money("headerDiscountAllocated");
                order.OrderDiscount = Money("orderDiscount"); order.ComboDiscountTotal = Money("orderComboDiscount"); order.VoucherDiscountTotal = Money("orderVoucherDiscount");
                order.ComboPromotionId = Id("orderComboPromotionId");
                Need(line.LineDiscount + line.PromotionDiscount + Money("headerDiscountAllocated") == web.Discount, "Chi tiết vai trò ưu đãi không cân tổng giảm giá đã chốt.");
            }
            else Need(!meta.TryGetProperty("programs", out var programs) || programs.GetArrayLength() == 0, "Đơn ưu đãi cũ thiếu bằng chứng vai trò chương trình; chưa tự gán mã ưu đãi để ghi bán.");
            order.Lines.Add(line); mapped.Add(web.Id, line); order.Subtotal += web.NetAmount + web.Discount; order.DiscountTotal += web.Discount;
            foreach (var sourceLine in fragments)
            {
                var child = children.Single(x => x.Id == sourceLine.DeliveryOrderId); var source = sources.Single(x => x.Id == child.StorefrontFulfillmentSourceId);
                var baseQty = sourceLine.StorefrontBaseQuantity!.Value; var selling = baseQty / web.BaseMultiplier;
                var orderedFragments = fragments.OrderBy(x => children.Single(c => c.Id == x.DeliveryOrderId).StorefrontFulfillmentSourceId).ThenBy(x => x.Id).ToList();
                var startBase = orderedFragments.TakeWhile(x => x.Id != sourceLine.Id).Sum(x => x.StorefrontBaseQuantity!.Value);
                var lineDiscount = line.LineDiscount == web.Discount && !meta.TryGetProperty("roleVersion", out _) ? sourceLine.LineDiscount : StorefrontFulfillmentService.SliceMoney(line.LineDiscount, web.BaseQuantity, startBase, baseQty);
                var promotionDiscount = StorefrontFulfillmentService.SliceMoney(line.PromotionDiscount, web.BaseQuantity, startBase, baseQty);
                var header = sourceLine.LineDiscount - lineDiscount - promotionDiscount;
                Need(header >= 0, "Phân bổ vai trò giảm giá của nguồn không khớp.");
                var useBase = decimal.Round(selling, 4) != selling || decimal.Round(selling * web.BaseMultiplier, 4, MidpointRounding.AwayFromZero) != baseQty;
                var transactions = costs[sourceLine.Id].Slices.Select(x => x.InventoryTransactionId).Distinct().ToList(); Need(transactions.Count == 1, "Dòng nguồn không có một chứng từ xuất xác định.");
                if (Eligible(web))
                {
                    var invoiceQuantity = useBase ? baseQty : selling;
                    var invoicePrice = decimal.Round(sourceLine.Net / invoiceQuantity, 2, MidpointRounding.AwayFromZero);
                    Need(invoicePrice >= 0 && invoicePrice < StorefrontValues.MoneyLimit && decimal.Round(invoiceQuantity * invoicePrice, 0, MidpointRounding.AwayFromZero) == sourceLine.Net,
                        "Đơn đã giao nhưng đơn vị hóa đơn không biểu diễn đúng số tiền nguồn theo chính sách nhà cung cấp. Chưa ghi bán; cần cấu hình đơn vị/chứng từ phù hợp.");
                }
                order.LegalEntityAllocations.Add(new() { StoreId = store, OrderLine = line, ProductVariantId = web.VariantId,
                    ProductUnitConversionId = useBase ? null : web.ProductUnitConversionId, WarehouseId = source.SourceWarehouseId, LegalEntityId = source.SourceLegalEntityId,
                    SalePriority = originalPriorities[source.Id], InventoryTransactionId = transactions.Single(),
                    Quantity = useBase ? baseQty : selling, BaseQuantity = baseQty, InvoiceUnitKind = useBase ? OrderAllocationInvoiceUnitKind.NativeBaseUnit : OrderAllocationInvoiceUnitKind.SellingUnit,
                    InvoiceUnitId = useBase ? sourceLine.BaseUnitId : web.SellingUnitId, InvoiceUnitName = useBase ? sourceLine.BaseUnitName : web.UnitName,
                    UnitPrice = web.UnitPrice, LineTotal = sourceLine.Gross - lineDiscount - promotionDiscount,
                    DiscountAllocated = lineDiscount, PromotionDiscountAllocated = promotionDiscount, StorefrontHeaderDiscountAllocated = header, NetAmount = sourceLine.Net,
                    AllocationSource = costs[sourceLine.Id].CostQuality == "Confirmed" ? OrderLegalEntityAllocationSource.AutoBySalePriority : OrderLegalEntityAllocationSource.AutoNegativeFallback,
                    Note = "Nguồn website #" + source.Id + "; nguyên chứng từ xuất giao #" + child.Id });
            }
        }
        order.PromotionDiscountTotal = order.Lines.Sum(x => x.PromotionDiscount);
        foreach (var invoiceOwner in order.LegalEntityAllocations.Where(x => x.OrderLine.HasInputInvoiceSnapshot == true).GroupBy(x => x.LegalEntityId))
            Need(invoiceOwner.Sum(x => x.Quantity) < 1_000_000_000_000_000m, "Tổng số lượng hóa đơn nguồn vượt độ chính xác hỗ trợ. Đơn đã giao chưa ghi bán; cần đối soát chứng từ phù hợp.");
        var withholdingOwners = await db.Set<StorefrontCarrierWithholding>().AsNoTracking().Where(x => x.StoreId == store && x.StorefrontOrderId == parent.Id).ToDictionaryAsync(x => x.Id, x => x.LegalEntityId, ct);
        var funding = FundingPlan(order, parent, fee, payments, withholdingOwners);
        if (fee is not null) fee.CashOnlyFunding = funding.Where(x => x.ServiceAmount > 0).All(x => x.Source.Method == PaymentMethod.Cash);
        db.Orders.Add(order); await db.SaveChangesAsync(ct);
        foreach (var web in webLines.Where(x => x.IsGift))
        {
            using var promo = JsonDocument.Parse(pricing[web.Id].PromotionSnapshotJson);
            if (promo.RootElement.TryGetProperty("giftSourceLineId", out var original) && original.ValueKind == JsonValueKind.Number)
            { var source = pricing.SingleOrDefault(x => x.Value.Index == original.GetInt32()); Need(source.Value is not null, "Quà thiếu dòng mua gốc đã chốt."); mapped[web.Id].GiftSourceLineId = mapped[source.Key].Id; }
        }
        await db.SaveChangesAsync(ct);
        db.Add(new StorefrontSaleBinding { StoreId = store, StorefrontOrderId = parent.Id, SaleOrderId = order.Id, SettlingShiftId = shift.Id,
            ActorUserId = actor, ClientRequestId = requestId, SourceSnapshotJson = JsonSerializer.Serialize(new { parent.Id, parent.Code, actualSources = sources.Select(x => x.Id) }, Json) });
        await db.SaveChangesAsync(ct);
        if (fee is not null) { db.Add(fee); await db.SaveChangesAsync(ct); }
        foreach (var child in children)
            db.Add(new StorefrontSettlementLink { StoreId = store, StorefrontOrderId = parent.Id, FulfillmentSourceId = child.StorefrontFulfillmentSourceId!.Value,
                SaleOrderId = order.Id, SettlingShiftId = shift.Id, SettledByUserId = actor, GoodsAmount = child.QuotedTotal,
                AllocationSnapshotJson = JsonSerializer.Serialize(new { child.Id, lineIds = child.Lines.Select(x => x.Id), goodsOrderId = order.Id }, Json) });
        await db.SaveChangesAsync(ct);
        foreach (var slice in funding)
        {
            var source = slice.Source; var amount = slice.GoodsAmount;
            if (amount > 0) order.Payments.Add(new() { StoreId = store, Amount = amount, Method = source.Method, PaidAtUtc = source.AtUtc,
                BankVerifiedReceiptId = source.GlobalId, StorefrontCodReceiptId = source.CodId, StorefrontCarrierWithholdingId = source.WithholdingId,
                Provider = "STOREFRONT_SOURCE", ReferenceCode = source.Reference, MetadataJson = JsonSerializer.Serialize(new { parent.Id, parent.Code }, Json) });
            if (source.Amount > amount)
            {
                Need(fee is not null, "Khoản phân bổ phí thiếu nguồn dịch vụ.");
                db.Add(new StorefrontServiceFunding { StoreId = store, StorefrontOrderId = parent.Id, ServiceRevenueId = fee!.Id,
                    GlobalBankReceiptId = source.GlobalId, CodReceiptId = source.CodId, CarrierWithholdingId = source.WithholdingId, Amount = source.Amount - amount });
            }
        }
        await db.SaveChangesAsync(ct);
        foreach (var guard in finalizeGuards) await guard.ValidateAsync(order, ct);
        order.OrderNumber = await numbers.NextAsync(ct); order.Status = OrderStatus.Completed; order.PaymentStatus = PaymentStatus.Paid; order.CompletedAtUtc = now;
        if (order.Lines.Any(x => x.IsProvisionalCost))
        {
            order.HasInventoryIssue = true; order.InventoryResolutionStatus = InventoryResolutionStatus.PendingResolution; order.InventoryIssueOpenedAtUtc = now;
            var issue = new OrderInventoryIssue { StoreId = store, OrderId = order.Id, Code = "WEB-" + order.Id, OpenedAtUtc = now, DueAtUtc = now.AddDays(1), InternalNote = "Giá vốn nguồn xuất giao website còn tạm tính; không xuất lại." };
            db.OrderInventoryIssues.Add(issue); await db.SaveChangesAsync(ct);
            foreach (var line in order.Lines.Where(x => x.IsProvisionalCost))
            {
                var txIds = order.LegalEntityAllocations.Where(x => x.OrderLineId == line.Id).Select(x => x.InventoryTransactionId!.Value).ToHashSet();
                var openSources = valuation.Roots.Where(x => txIds.Contains(x.InventoryTransactionId)).SelectMany(root => root.CostLayerAllocations
                    .Where(a => a.IsProvisional && a.ResolvedQuantity < a.Quantity).Select(a => new { Root = root, Allocation = a })).ToList();
                Need(openSources.Count > 0, "Giá vốn chưa xác định nhưng thiếu chính nguồn giữ ngân sách tạm tính; chưa ghi bán.");
                var row = new OrderInventoryIssueLine { StoreId = store, OrderInventoryIssueId = issue.Id, OrderId = order.Id, OrderLineId = line.Id,
                    ProductId = line.ProductId, ProductVariantId = line.VariantId, ProductUnitConversionId = line.ProductUnitConversionId,
                    OrderedQty = line.BaseQuantity, NegativeQty = openSources.Sum(x => x.Allocation.Quantity - x.Allocation.ResolvedQuantity),
                    ProvisionalCostAmount = openSources.Sum(x => GaoApp.Application.Services.Inventory.InventoryCostBudgetPolicy.Slice(x.Allocation.Quantity, x.Allocation.Amount,
                        x.Allocation.ResolvedQuantity, x.Allocation.Quantity - x.Allocation.ResolvedQuantity)) };
                row.ProvisionalUnitCost = decimal.Round(row.ProvisionalCostAmount!.Value / row.NegativeQty, 2, MidpointRounding.AwayFromZero);
                db.Add(row); await db.SaveChangesAsync(ct);
                foreach (var source in openSources) db.Add(new StorefrontInventoryIssueSource { StoreId = store, StorefrontOrderId = parent.Id,
                    OrderInventoryIssueId = issue.Id, OrderInventoryIssueLineId = row.Id, InventoryValuationEntryId = source.Root.Id,
                    InventoryCostLayerAllocationId = source.Allocation.Id, BaselineResolvedQuantity = source.Allocation.ResolvedQuantity,
                    UnresolvedQuantity = source.Allocation.Quantity - source.Allocation.ResolvedQuantity });
            }
        }
        if (notifications is not null) await notifications.CaptureFinalizedOrderAsync(order, null, ct);
        db.Add(new StorefrontInvoiceWork { StoreId = store, StorefrontOrderId = parent.Id, SaleOrderId = order.Id, ServiceRevenueId = fee?.Id, NextAttemptAtUtc = now });
        await db.SaveChangesAsync(ct); return order.Id;
    }
    private async Task RequireInvoiceOwner(int store, int owner, CancellationToken ct)
    {
        var entity = await db.LegalEntities.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == store && x.Id == owner, ct);
        var setting = entity?.InvoiceProviderSettingId is int id ? await db.InvoiceProviderSettings.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == store && x.Id == id, ct) : null;
        var error = InvoiceCommandService.ValidateProviderSetting(setting, entity);
        if (error is not null) throw new ConflictAppException(error.Message + " Đơn đã giao vẫn được giữ, chưa ghi bán.");
    }
    private async Task<List<PaymentSource>> PaymentSources(StorefrontOrder parent, CancellationToken ct)
    {
        if (parent.PaymentMethod == StorefrontPaymentMethod.Qr)
        {
            var local = await (from a in db.Set<StorefrontPaymentAllocation>() join r in db.Set<StorefrontBankReceipt>() on a.BankReceiptId equals r.Id
                where a.StoreId == parent.StoreId && a.StorefrontOrderId == parent.Id select r).SingleAsync(ct);
            var receipt = await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x => x.Id == local.GlobalBankReceiptId && x.OriginStoreId == parent.StoreId, ct);
            Need(receipt.AssetConfidence == BankReceiptAssetConfidence.CertifiedDistinct && !receipt.NeedsReview, "Khoản QR đang cần đối soát.");
            return [new(receipt.Amount, receipt.Id, null, null, PaymentMethod.BankTransfer, receipt.OccurredAtUtc!.Value, receipt.ProviderReference)];
        }
        var receipts = await db.Set<StorefrontCodReceipt>().AsNoTracking().Where(x => x.StoreId == parent.StoreId && x.StorefrontOrderId == parent.Id).OrderBy(x => x.Id).ToListAsync(ct);
        var result = new List<PaymentSource>();
        foreach (var source in receipts)
        {
            if (source.GlobalBankReceiptId.HasValue)
            {
                var r = await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x => x.Id == source.GlobalBankReceiptId && x.OriginStoreId == parent.StoreId, ct);
                Need(r.AssetConfidence == BankReceiptAssetConfidence.CertifiedDistinct && r.RoutingProvenance == BankReceiptRoutingProvenance.UnmatchedOnly &&
                    r.Channel == BankPaymentChannel.Unknown && r.ReviewCode == "UNMATCHED_CHANNEL_BINDING" && r.TimePrecision != BankTimePrecision.Unknown && r.OccurredAtUtc.HasValue &&
                    (r.Status is "PAID" or "SUCCESS" or "COMPLETED" or "APPROVED") &&
                    !await db.Set<BankReceiptConflict>().AnyAsync(x => x.BankVerifiedReceiptId == r.Id, ct) &&
                    !await db.Set<BankReceiptConsumption>().AnyAsync(x => x.BankVerifiedReceiptId == r.Id, ct), "Khoản COD đã có mâu thuẫn hoặc đã được dùng; dừng ghi bán.");
                var routing = await db.Set<StorefrontCodBankRouting>().AsNoTracking().SingleAsync(x => x.StoreId == parent.StoreId && x.StorefrontOrderId == parent.Id && x.Id == source.CodBankRoutingId && x.GlobalBankReceiptId == r.Id, ct);
                var account = await db.StoreBankAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == parent.StoreId && x.Id == r.BankAccountId && x.IsActive, ct);
                Need(account is not null && GaoApp.Infrastructure.Services.Banking.GlobalBankReceiptLedger.AccountIdentity(account.BankCode, account.AccountNumber) == r.AccountIdentity && Convert.ToBase64String(account.RowVersion) == r.AccountVersion, "Tài khoản gốc của khoản COD đã thay đổi; chưa ghi bán.");
                var owners = await db.Set<StorefrontFulfillmentSource>().AsNoTracking().Where(x => x.StoreId == parent.StoreId && x.StorefrontOrderId == parent.Id).Select(x => x.SourceLegalEntityId).Distinct().ToListAsync(ct);
                if (parent.ShippingCharge > 0) owners.Add((await StorefrontServiceSource.ReadAsync(db, parent, ct)).LegalEntityId);
                owners = owners.Distinct().ToList();
                var original = JsonSerializer.Deserialize<List<BankCollectionAuthorityProof>>(r.NativeCollectionAuthorityProofJson ?? "[]") ?? [];
                var authorities = await db.Set<StorefrontBankCollectionAuthority>().AsNoTracking().Where(x => x.StoreId == parent.StoreId && x.BankAccountId == r.BankAccountId && x.ReceivingLegalEntityId == routing.ReceivingLegalEntityId && x.IsActive && owners.Contains(x.SourceLegalEntityId)).ToListAsync(ct);
                Need(authorities.Select(x => x.SourceLegalEntityId).Distinct().Count() == owners.Count && authorities.All(x => original.Any(p => p.Id == x.Id && p.BankAccountId == x.BankAccountId && p.ReceivingLegalEntityId == x.ReceivingLegalEntityId && p.SourceLegalEntityId == x.SourceLegalEntityId && p.Version == Convert.ToBase64String(x.RowVersion))), "Quyền thu gốc cho hàng và dịch vụ không còn đầy đủ; chưa ghi bán.");
                if (r.IdentityKind == BankExternalIdentityKind.BankNotification)
                {
                    var sourceReceipt = await (from c in db.Set<StorefrontNotificationCertification>() join a in db.Set<StorefrontNotificationAttestation>() on c.NotificationAttestationId equals a.Id
                        join n in db.Set<BankNotificationReceipt>() on a.BankNotificationReceiptId equals n.Id
                        where c.StoreId == parent.StoreId && c.GlobalBankReceiptId == r.Id select new { a.HadManualAmbiguity, n.PaymentId, n.AppliedWaitId }).SingleOrDefaultAsync(ct);
                    Need(sourceReceipt is { HadManualAmbiguity: false, PaymentId: null, AppliedWaitId: null }, "Nguồn thông báo có tranh chấp hoặc đã gắn thanh toán khác.");
                }
                result.Add(new(source.Amount, null, source.Id, null, PaymentMethod.BankTransfer, r.OccurredAtUtc!.Value, r.ExternalTransactionId));
            }
            else
            {
                var cash = await db.POSShiftCashTransactions.AsNoTracking().SingleAsync(x => x.StoreId == parent.StoreId && x.Id == source.POSShiftCashTransactionId, ct);
                Need(cash.Type == POSShiftCashTransactionType.CashIn && cash.Amount == source.Amount, "Chứng cứ nộp két COD đã thay đổi.");
                Need(cash.CustomerDebtReceiptId == null && cash.CustomerDepositEntryId == null &&
                    !await db.Set<POSCashAdjustmentRequest>().AnyAsync(x => x.StoreId == parent.StoreId && x.TransactionId == cash.Id && x.Status == POSCashAdjustmentStatus.Pending, ct) &&
                    !await db.TreasuryEntries.IgnoreQueryFilters().AnyAsync(x => x.StoreId == parent.StoreId && x.POSShiftCashTransactionId == cash.Id && (x.TargetFund != null || x.ReversalOfId != null || x.ReversedAtUtc != null || x.Amount != cash.Amount || x.Fund != "cash" || x.OperatingExpenseId != null || x.PurchasePayableId != null || x.IsDeleted)), "Tiền thực nộp COD không còn nguyên nguồn bên ngoài.");
                result.Add(new(source.Amount, null, source.Id, null, PaymentMethod.Cash, cash.CreatedAtUtc, "COD-CASH-" + source.Id));
            }
        }
        var withheld = await db.Set<StorefrontCarrierWithholding>().AsNoTracking().Where(x => x.StoreId == parent.StoreId && x.StorefrontOrderId == parent.Id).OrderBy(x => x.Id).ToListAsync(ct);
        foreach (var deduction in withheld)
            Need(await db.OperatingExpenses.AnyAsync(x => x.StoreId == parent.StoreId && x.Id == deduction.OperatingExpenseId && x.Status == "confirmed" && x.IsPaid && x.PaymentMethod == "carrier-withholding" && x.Amount == deduction.Amount, ct) &&
                !await db.TreasuryEntries.AnyAsync(x => x.StoreId == parent.StoreId && x.OperatingExpenseId == deduction.OperatingExpenseId, ct), "Khoản hãng giao giữ lại không còn nguyên chứng từ quyết toán.");
        result.AddRange(withheld.Select(x => new PaymentSource(x.Amount, null, null, x.Id, PaymentMethod.Other, x.CreatedAtUtc, "COD-WITHHOLD-" + x.Id)));
        return result;
    }
}
