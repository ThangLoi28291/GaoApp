using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.DTOs.POS;
using GaoApp.Application.Interfaces.Services.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Orders;

public sealed partial class POSService
{
    private readonly IDeliverySaleCostEvidence? _deliverySaleEvidence;
    public async Task<DeliveryHandoverCounterDto?> GetDeliveryCounterAsync(CancellationToken ct = default)
    {
        if (!_posContext.IsAvailable || _posContext.StoreId <= 0 || _posContext.TerminalId <= 0 || _posContext.UserId is not > 0) return null;
        var shift = await _shifts.GetOpenShiftAsync(_posContext.StoreId, _posContext.TerminalId, ct);
        if (shift is null || shift.OpenedByUserId != _posContext.UserId || shift.Status != POSShiftStatus.Open) return null;
        return new(shift.StoreId, shift.TerminalId, shift.Id, shift.WarehouseId, shift.OpenedByUserId, true)
        { TerminalName = shift.Terminal?.Name };
    }

    // Only the locked delivery aggregate calls this server-owned plan. No HTTP sale/pricing DTO reaches it.
    public async Task<DeliverySaleFinalizeResult> FinalizeDeliverySaleAsync(DeliverySaleFinalizePlan plan, CancellationToken ct = default)
    {
        if (plan.StoreId != _posContext.StoreId || plan.ActorUserId != _posContext.UserId || plan.TerminalId != _posContext.TerminalId ||
            plan.ClientRequestId == Guid.Empty || plan.Amount <= 0 || plan.Lines.Count == 0 || plan.Lines.Sum(x => x.Net) != plan.Amount)
            throw new BusinessRuleException("DELIVERY_SALE_PLAN_INVALID: Kế hoạch bán không thuộc quầy/người chốt hoặc không cân tiền.");
        var shift = await RequireCurrentOpenShiftAsync(ct); EnsureShiftOwnership(shift);
        if (shift.Id != plan.ShiftId || shift.StoreId != plan.StoreId || shift.OpenedByUserId != plan.ActorUserId || shift.Status != POSShiftStatus.Open)
            throw new ConflictAppException("Ca chốt giao hàng đã thay đổi.");
        var source = await _orders.GetByIdAsync(plan.SourceOrderId, ct) ?? throw new BusinessRuleException("Thiếu giỏ nguồn giao hàng.");
        if (source.StoreId != plan.StoreId || source.CustomerId != plan.SourceCustomerId)
            throw new BusinessRuleException("Giỏ/khách nguồn không khớp hồ sơ giao.");
        var sourceWarehouse = await _warehouses.GetByIdAsync(plan.SourceWarehouseId, ct);
        if (sourceWarehouse is null || sourceWarehouse.StoreId != plan.StoreId || sourceWarehouse.LegalEntityId != plan.SourceLegalEntityId ||
            sourceWarehouse.LegalEntity is null || sourceWarehouse.LegalEntity.StoreId != plan.StoreId ||
            sourceWarehouse.LegalEntity.Id != plan.SourceLegalEntityId || sourceWarehouse.LegalEntity.SalePriority <= 0)
            throw new BusinessRuleException("DELIVERED_SOURCE_INVALID: Kho/chủ thể hoặc thứ tự bán nguồn không hợp lệ.");
        await using var tx = await _uow.BeginTransactionAsync(ct);
        try
        {
            var order = new Order { StoreId = plan.StoreId, POSShiftId = shift.Id, CustomerId = source.CustomerId,
                Customer = source.Customer, Note = $"Bàn giao sau giao hàng #{plan.DeliveryOrderId}; giỏ nguồn #{source.Id}",
                GrandTotal = plan.Amount, PaidTotal = plan.Cash + plan.Transfer, BalanceDue = plan.Credit,
                UseMultiLegalEntity = true, LegalEntityModeCapturedAtUtc = plan.RecordedAtUtc.UtcDateTime,
                LegalEntityCount = 1, LegalEntityAllocatedAtUtc = plan.RecordedAtUtc.UtcDateTime,
                InvoiceIssuanceRoute = source.InvoiceIssuanceRoute,
                InvoiceIssuanceRouteSelectedAtUtc = source.InvoiceIssuanceRouteSelectedAtUtc,
                InvoiceIssuanceRouteSelectedByUserId = source.InvoiceIssuanceRouteSelectedByUserId,
                ComboPromotionId = source.ComboPromotionId, ComboPromotionName = source.ComboPromotionName,
                ComboPromotionNote = source.ComboPromotionNote };
            var mapped = new Dictionary<int, OrderLine>();
            foreach (var line in plan.Lines.Where(x => x.DeliveredBaseQuantity > 0))
            {
                var original = source.Lines.SingleOrDefault(x => x.Id == line.SourceOrderLineId && x.VariantId == line.VariantId);
                if (line.ProductId <= 0 || original is not null && original.ProductId != line.ProductId)
                    throw new BusinessRuleException("Biến thể/sản phẩm nguồn không khớp.");
                var quantity = DeliveryHandoverPolicy.SaleQuantity(line.DeliveredBaseQuantity, line.SourceMultiplier);
                var sourceBase = line.SourceOrderedQuantity * line.SourceMultiplier;
                var lineDiscount = Math.Min(line.Discount, DeliveryValues.RoundMoney(line.SourceLineDiscount * line.DeliveredBaseQuantity / sourceBase));
                var promotion = original is null || original.LineDiscount + original.PromotionDiscount == 0 ? 0 :
                    Math.Min(lineDiscount, DeliveryValues.RoundMoney(lineDiscount * original.PromotionDiscount / (original.LineDiscount + original.PromotionDiscount)));
                var header = line.Discount - lineDiscount;
                var headerWeight = source.ComboDiscountTotal + source.OrderDiscount + source.VoucherDiscountTotal;
                var combo = headerWeight == 0 ? 0 : Math.Min(header, DeliveryValues.RoundMoney(header * source.ComboDiscountTotal / headerWeight));
                var voucher = headerWeight == 0 ? 0 : Math.Min(header - combo, DeliveryValues.RoundMoney(header * source.VoucherDiscountTotal / headerWeight));
                var orderDiscount = header - combo - voucher;
                var target = new OrderLine { StoreId = plan.StoreId, ProductId = line.ProductId, VariantId = line.VariantId,
                    ItemName = line.ItemName, Sku = original?.Sku, Barcode = original?.Barcode,
                    Quantity = quantity.Quantity, Multiplier = quantity.Multiplier, BaseQuantity = line.DeliveredBaseQuantity,
                    SellingUnitId = quantity.UsesBaseUnit ? line.BaseUnitId : line.SellingUnitId,
                    ProductUnitConversionId = quantity.UsesBaseUnit ? null : line.ProductUnitConversionId,
                    SellingUnitName = quantity.UsesBaseUnit ? line.BaseUnitName : line.UnitName,
                    UnitName = quantity.UsesBaseUnit ? line.BaseUnitName : line.UnitName,
                    BaseUnitId = line.BaseUnitId, BaseUnitName = line.BaseUnitName,
                    UnitPrice = decimal.Round(line.Gross / quantity.Quantity, 2, MidpointRounding.AwayFromZero),
                    OriginalUnitPrice = line.SourceUnitPrice, LineDiscount = lineDiscount - promotion,
                    PromotionDiscount = promotion, LineTotal = line.Gross - lineDiscount,
                    PromotionId = original?.PromotionId, PromotionName = original?.PromotionName, PromotionType = original?.PromotionType,
                    ComboPromotionId = original?.ComboPromotionId, ComboPromotionName = original?.ComboPromotionName,
                    ComboPromotionNote = original?.ComboPromotionNote, ComboAllocatedDiscount = combo,
                    PromotionBuyQuantity = original?.PromotionBuyQuantity ?? 0, PromotionGiftQuantity = original?.PromotionGiftQuantity ?? 0,
                    IsPromotionGift = original?.IsPromotionGift ?? false, GiftPromotionId = original?.GiftPromotionId,
                    GiftPromotionName = original?.GiftPromotionName, GiftPromotionNote = original?.GiftPromotionNote,
                    RewardBaseUnitPrice = original?.RewardBaseUnitPrice,
                    LineCostTotal = decimal.Round(line.RecordedCost, 2, MidpointRounding.AwayFromZero),
                    UnitCostSnapshot = decimal.Round(line.RecordedCost / line.DeliveredBaseQuantity, 2, MidpointRounding.AwayFromZero),
                    GrossProfit = line.Net - decimal.Round(line.RecordedCost, 2, MidpointRounding.AwayFromZero),
                    IsProvisionalCost = line.CostQuality != "Confirmed",
                    ProvisionalUnitCost = line.CostQuality != "Confirmed" ? decimal.Round(line.RecordedCost / line.DeliveredBaseQuantity, 2, MidpointRounding.AwayFromZero) : null,
                    CostSnapshotNote = $"delivery-handover-v1:{plan.DeliveryOrderId}; {line.CostQuality}; source quote/slices retained in immutable delivery history" };
                order.Lines.Add(target); mapped[line.DeliveryLineId] = target;
                order.Subtotal += line.Gross; order.DiscountTotal += lineDiscount; order.PromotionDiscountTotal += promotion;
                order.ComboDiscountTotal += combo; order.OrderDiscount += orderDiscount; order.VoucherDiscountTotal += voucher;
                var sourceTransactions = line.Slices.Select(x => x.InventoryTransactionId).Distinct().ToArray();
                if (sourceTransactions.Length != 1) throw new BusinessRuleException("DELIVERED_SOURCE_INVALID: Dòng thực giao không có một chứng từ xuất nguồn.");
                order.LegalEntityAllocations.Add(new OrderLegalEntityAllocation { StoreId = plan.StoreId, OrderLine = target,
                    ProductVariantId = line.VariantId, ProductUnitConversionId = target.ProductUnitConversionId,
                    LegalEntityId = plan.SourceLegalEntityId, WarehouseId = plan.SourceWarehouseId,
                    SalePriority = sourceWarehouse.LegalEntity.SalePriority,
                    InventoryTransactionId = sourceTransactions[0], Quantity = target.Quantity, BaseQuantity = target.BaseQuantity,
                    UnitPrice = target.UnitPrice, LineTotal = target.LineTotal, DiscountAllocated = target.LineDiscount,
                    PromotionDiscountAllocated = promotion, ComboDiscountAllocated = combo, OrderDiscountAllocated = orderDiscount,
                    VoucherDiscountAllocated = voucher, NetAmount = line.Net,
                    AllocationSource = line.CostQuality == "Confirmed" ? OrderLegalEntityAllocationSource.AutoBySalePriority : OrderLegalEntityAllocationSource.AutoNegativeFallback,
                    Note = $"delivery-handover-v1:{plan.DeliveryOrderId}; original dispatch; no second stock export" });
            }
            foreach (var applied in source.RewardVouchers.Where(x => !x.IsDeleted))
                order.RewardVouchers.Add(new OrderRewardVoucher { StoreId = plan.StoreId, VoucherId = applied.VoucherId,
                    Voucher = applied.Voucher, VoucherValue = applied.VoucherValue });
            order.DiscountTotal = order.Subtotal - order.GrandTotal;
            if (plan.Cash + plan.Transfer > 0)
                order.Payments.Add(new OrderPayment { StoreId = plan.StoreId, ClientRequestId = plan.ClientRequestId,
                    Method = plan.Cash > 0 ? PaymentMethod.Cash : PaymentMethod.BankTransfer, Amount = plan.Cash + plan.Transfer,
                    PaidAtUtc = plan.RecordedAtUtc.UtcDateTime, ReferenceCode = plan.BankReference,
                    Provider = plan.Transfer > 0 ? "DELIVERY_CONFIRMED" : null,
                    MetadataJson = JsonSerializer.Serialize(new { deliveryOrderId = plan.DeliveryOrderId, bankAccountId = plan.BankAccountId, actorUserId = plan.ActorUserId }) });
            await _orders.AddAsync(order, ct); await _orders.SaveChangesAsync(ct);
            foreach (var line in plan.Lines.Where(x => x.DeliveredBaseQuantity > 0))
            {
                var original = source.Lines.SingleOrDefault(x => x.Id == line.SourceOrderLineId && x.VariantId == line.VariantId);
                if (original?.GiftSourceLineId is int purchaseId)
                {
                    var purchase = plan.Lines.FirstOrDefault(x => x.SourceOrderLineId == purchaseId && mapped.ContainsKey(x.DeliveryLineId));
                    if (purchase is not null) mapped[line.DeliveryLineId].GiftSourceLineId = mapped[purchase.DeliveryLineId].Id;
                }
            }
            if (plan.Credit > 0)
                await (_receivables ?? throw new InvalidOperationException("Receivable service unavailable.")).ValidateCreditAsync(order,
                    new FinalizeCreditRequest { ClientRequestId = plan.ClientRequestId, ExpectedCustomerId = source.CustomerId ?? 0,
                        ExpectedBalance = plan.Credit, DueDate = plan.CreditDueDate?.ToDateTime(TimeOnly.MinValue), Note = plan.CreditNote }, ct);
            foreach (var guard in _finalizeGuards) await guard.ValidateAsync(order, ct);
            order.Status = OrderStatus.Completed; order.PaymentStatus = plan.Credit > 0 ? PaymentStatus.Unpaid : PaymentStatus.Paid;
            order.CompletedAtUtc = plan.RecordedAtUtc.UtcDateTime; order.CustomerPriceTierSnapshot = ResolveOrderPriceTier(order);
            if (_receivables is not null) await _receivables.PostSaleAsync(order, ct);
            MarkRewardVouchersAsUsed(order);
            var summary = new FinalizeInventorySummary { HasProvisionalCost = order.Lines.Any(x => x.IsProvisionalCost) };
            foreach (var line in order.Lines.Where(x => x.IsProvisionalCost))
                summary.IssueLines.Add(new FinalizeInventoryIssueLine { OrderLineId = line.Id, ProductId = line.ProductId, VariantId = line.VariantId,
                    ProductUnitConversionId = line.ProductUnitConversionId, ItemName = line.ItemName, BaseQuantity = line.BaseQuantity,
                    IsProvisionalCost = true, UnitCostSnapshot = line.UnitCostSnapshot, ProvisionalUnitCost = line.ProvisionalUnitCost, Note = line.CostSnapshotNote });
            await UpsertPendingInventoryIssueAsync(order, summary, ct);
            order.OrderNumber = await _orderNo.NextAsync(ct);
            await _orders.SaveChangesAsync(ct); await ApplyRewardForFinalizedOrderAsync(order, ct);
            ApplyPaymentsToShift(shift, order); // The unrelated CurrentOrderId remains untouched.
            await _orders.SaveChangesAsync(ct);
            await WritePosAuditLogAsync("ORDER_FINALIZED", order.Id, $"Bàn giao sau giao hàng #{plan.DeliveryOrderId}",
                new { plan.DeliveryOrderId, plan.SourceOrderId, plan.SourceWarehouseId, plan.SourceLegalEntityId, plan.ClientRequestId, order.GrandTotal }, ct);
            await WriteFinalizeOrderAuditLogAsync(order, OrderStatus.Draft.ToString(), PaymentStatus.Unpaid.ToString(), ct);
            await tx.CommitAsync(ct);
            await tx.AfterCommitAsync(token => TryGenerateInvoiceAfterFinalizeAsync(order.Id, token), ct);
            return new(order.Id);
        }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
    }
}
