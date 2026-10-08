using System.Globalization;
using System.Text.Json;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Delivery;

public sealed partial class DeliveryTripService
{
    private static string Dong(decimal value) => value.ToString("0", CultureInfo.InvariantCulture);
    private static DeliveryCapability FinanceCapability(string method) => DeliveryHandoverPolicy.Method(method) switch
    {
        DeliverySettlementMethod.Cash => DeliveryCapability.ReconcileCash,
        DeliverySettlementMethod.ConfirmedBankTransfer => DeliveryCapability.ConfirmBankTransfer,
        _ => DeliveryCapability.Credit
    };
    private static DeliveryHandoverQuantity[] HandoverQuantities(IReadOnlyList<OutcomeSource> sources,
        IReadOnlyList<DeliveryHandoverLineRequest>? lines)
    {
        if (lines is null || lines.Any(x => x is null) || !sources.Select(x => x.LineId).SequenceEqual(lines.Select(x => x.LineId).Order()))
            throw Error(400, "LINE_INVALID", "Cần đúng toàn bộ dòng đã xuất, không trùng.");
        var result = sources.Select(x => new DeliveryHandoverQuantity(x.LineId, x.Dispatched, x.AcceptedReturn,
            DeliveryOutcomePolicy.ParseQuantity(lines.Single(l => l.LineId == x.LineId).DeliveredBaseQuantityText),
            DeliveryOutcomePolicy.ParseQuantity(lines.Single(l => l.LineId == x.LineId).ActualReturnedBaseQuantityText))).ToArray();
        DeliveryHandoverPolicy.Quantities(result); return result;
    }
    private async Task<IReadOnlyList<DeliverySaleLinePlan>> HandoverPrices(DeliveryTripOrder member, DeliveryOrder order,
        IReadOnlyList<DeliveryHandoverQuantity> quantities, CancellationToken ct)
    {
        var work = await db.DeliveryPickingWorks.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.DeliveryOrderId == order.Id, ct);
        var picked = await db.DeliveryPickingLines.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == order.Id && x.IsActive && x.ApprovedQuantity > 0).ToListAsync(ct);
        Proof(work?.ApprovedRevision == member.AssignedApprovedRevision && work?.ApprovedTotal is not null &&
            picked.Count == quantities.Count && picked.Sum(x => x.ApprovedNet ?? -1) == work.ApprovedTotal);
        var result = new List<DeliverySaleLinePlan>();
        foreach (var quantity in quantities)
        {
            var quote = order.Lines.SingleOrDefault(x => x.Id == quantity.LineId);
            var picking = picked.SingleOrDefault(x => x.DeliveryOrderLineId == quantity.LineId);
            Proof(quote is not null && picking?.ApprovedQuantity is > 0 && picking.ApprovedNet is >= 0 &&
                quote.OrderedQuantity > 0 && quote.BaseMultiplier > 0 &&
                picking.ApprovedQuantity * quote.BaseMultiplier == quantity.Dispatched);
            var approvedGross = DeliveryValues.RoundMoney(quote!.Gross * picking!.ApprovedQuantity!.Value / quote.OrderedQuantity);
            var charge = DeliveryHandoverPolicy.Charge(quantity.Dispatched, quantity.Delivered, approvedGross, picking.ApprovedNet!.Value);
            var variant = await db.Set<ProductVariant>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == quote.VariantId, ct);
            Proof(variant is not null);
            result.Add(new(quantity.LineId, quote.SourceOrderLineId ?? 0, quote.VariantId, quantity.Delivered,
                charge.Gross, charge.Discount, charge.Net, 0, "Unknown", [])
            { ItemName = quote.ItemName, SellingUnitId = quote.SellingUnitId, BaseUnitId = quote.BaseUnitId,
                ProductUnitConversionId = quote.ProductUnitConversionId, UnitName = quote.UnitName,
                BaseUnitName = quote.BaseUnitName, SourceMultiplier = quote.BaseMultiplier,
                SourceUnitPrice = quote.UnitPrice, SourceOrderedQuantity = quote.OrderedQuantity,
                SourceLineDiscount = quote.LineDiscount, ProductId = variant!.ProductId,
                ApprovedGross = approvedGross, ApprovedNet = picking.ApprovedNet.Value });
        }
        return result;
    }
    private DeliveryHandoverPreviewDto HandoverPreview(DeliveryTrip trip, DeliveryOrder order,
        IReadOnlyList<DeliveryHandoverQuantity> quantities, IReadOnlyList<DeliverySaleLinePlan> prices)
        => new(trip.Id, order.Id, Version(trip.RowVersion), Version(order.RowVersion), order.SourceWarehouseId,
            order.SourceLegalEntityId, Hash(JsonSerializer.Serialize(new { order.Id, orderRevision = order.Revision, tripRevision = trip.Revision, quantities, prices }, Json)),
            Dong(prices.Sum(x => x.Net)), quantities.Select(q =>
            {
                var price = prices.Single(x => x.DeliveryLineId == q.LineId);
                return new DeliveryHandoverLinePreviewDto(q.LineId, Qty(q.Delivered), Qty(q.ActualReturned),
                    Qty(q.ActualReturned - q.Accepted), Dong(price.Gross), Dong(price.Discount), Dong(price.Net));
            }).ToArray());

    private async Task LockHandoverRead(int id, CancellationToken ct)
    {
        await DeliverySqlLock.AcquireAsync(db, $"trip:{Store}:{id}", ct);
        foreach (var orderId in (await AllBindings(id, ct)).Select(x => x.DeliveryOrderId).Distinct().Order())
            await DeliverySqlLock.AcquireAsync(db, $"order:{Store}:{orderId}", ct);
    }
    public async Task<DeliveryHandoverPreviewDto> PreviewHandoverAsync(int id, int orderId,
        DeliveryHandoverPreviewRequest request, CancellationToken ct = default)
    {
        await Authorize(DeliveryCapability.View, ct);
        if (db.Database.CurrentTransaction is not null) throw Error(409, "TRANSACTION_CONFLICT", "Xem tiền cần giao dịch đọc riêng.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockHandoverRead(id, ct);
        await Authorize(DeliveryCapability.View, ct);
        var trip = await Trip(id, false, ct); var members = await Members(id, false, ct); await DepartedProof(trip, members, ct);
        Match(request.ExpectedTripVersion, trip.RowVersion);
        var member = members.SingleOrDefault(x => x.DeliveryOrderId == orderId) ?? throw Error(404, "DELIVERY_NOT_FOUND", "Đơn không thuộc chuyến.");
        var order = await Order(orderId, false, false, ct); Match(request.ExpectedOrderVersion, order.RowVersion);
        if (!Reportable(order.State)) throw Error(409, "HANDOVER_LOCKED", "Đơn đã chốt hoặc chưa xuất phát.");
        var sources = await OutcomeSources(trip, member, order, ct);
        var quantities = HandoverQuantities(sources, request.Lines); var prices = await HandoverPrices(member, order, quantities, ct);
        var result = HandoverPreview(trip, order, quantities, prices); await tx.CommitAsync(ct); return result;
    }

    public async Task<DeliveryHandoverWorkspaceDto> GetHandoverAsync(int id, CancellationToken ct = default)
    {
        await Authorize(DeliveryCapability.View, ct);
        if (db.Database.CurrentTransaction is not null) throw Error(409, "TRANSACTION_CONFLICT", "Đọc bàn giao cần giao dịch riêng.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockHandoverRead(id, ct);
        await Authorize(DeliveryCapability.View, ct);
        var trip = await Trip(id, false, ct); var detail = await Project(trip, ct);
        var counter = saleFinalizer is null ? null : await saleFinalizer.GetDeliveryCounterAsync(ct);
        var capabilities = new DeliveryHandoverCapabilitiesDto(await Has(DeliveryCapability.Finalize, ct),
            await Has(DeliveryCapability.ReceiveReturns, ct), await Has(DeliveryCapability.ReconcileCash, ct),
            await Has(DeliveryCapability.ConfirmBankTransfer, ct), await Has(DeliveryCapability.Credit, ct));
        var banks = await db.Set<StoreBankAccount>().AsNoTracking().Where(x => x.StoreId == Store && x.IsActive)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Id)
            .Select(x => new DeliveryHandoverBankDto(x.Id, x.BankName + " · " + x.AccountNumber)).ToListAsync(ct);
        var members = await Members(id, false, ct); var rows = new List<DeliveryHandoverOrderDto>();
        var canCost = await permissions.HasPermissionAsync(Store, Actor, GaoApp.Application.Common.Security.PermissionCodes.Inventory.Valuation.View, ct);
        foreach (var row in detail.Orders.Where(x => x.IsActive))
        {
            var order = await Order(row.DeliveryOrderId, false, false, ct);
            var saved = await HandoverSnapshot(order.Id, null, ct);
            var customerEligible = order.CustomerId.HasValue && await db.Customers.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Id == order.CustomerId && x.IsActive && x.HaveDebt, ct);
            DeliveryHandoverPreviewDto? preview = null;
            if (trip.State == DeliveryTripState.Departed && row.Outcome is { IsCurrentValid: true } && Reportable(order.State))
            {
                var source = await OutcomeSources(trip, members.Single(x => x.DeliveryOrderId == order.Id), order, ct);
                var quantities = HandoverQuantities(source, row.Outcome.Lines.Select(x => new DeliveryHandoverLineRequest(x.LineId,
                    x.DeliveredBaseQuantityText, x.ReportedReturnBaseQuantityText)).ToArray());
                preview = HandoverPreview(trip, order, quantities, await HandoverPrices(members.Single(x => x.DeliveryOrderId == order.Id), order, quantities, ct));
            }
            rows.Add(new(order.Id, order.CustomerId, customerEligible, counter is { IsOpen: true } && capabilities.CanFinalize &&
                trip.State == DeliveryTripState.Departed && Reportable(order.State), preview, saved?.Settlement,
                canCost && saved is not null ? saved.SaleLines.Any(x => x.CostQuality == "Unknown") ? "Unknown" :
                    saved.SaleLines.Any(x => x.CostQuality == "Provisional") ? "Provisional" : "Confirmed" : null));
        }
        await tx.CommitAsync(ct); return new(detail, counter, capabilities, banks, rows);
    }

    private async Task<DeliveryHandoverCounterDto> ClosingCounter(DeliveryHandoverRequest request, CancellationToken ct)
    {
        if (request.ExpectedTerminalId <= 0 || request.ExpectedShiftId <= 0 || saleFinalizer is null || handoverUow is null || saleCostEvidence is null)
            throw Error(409, "CLOSING_COUNTER_FORBIDDEN", "Cần quầy/ca đang mở của người chốt.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM POSShifts WITH (UPDLOCK,HOLDLOCK) WHERE StoreId={Store} AND Id={request.ExpectedShiftId} AND TerminalId={request.ExpectedTerminalId};", ct);
        foreach (var tracked in db.ChangeTracker.Entries<POSShift>().Where(x => x.Entity.StoreId == Store && x.Entity.Id == request.ExpectedShiftId).ToArray())
        {
            if (tracked.State != EntityState.Unchanged) throw Error(409, "TRACKED_STATE_DIRTY", "Ca có thay đổi chưa lưu.");
            await tracked.ReloadAsync(ct);
        }
        var counter = await saleFinalizer.GetDeliveryCounterAsync(ct);
        if (counter is null || counter.StoreId != Store || counter.TerminalId != request.ExpectedTerminalId || counter.ShiftId != request.ExpectedShiftId ||
            counter.OwnerUserId != Actor || !counter.IsOpen || counter.WarehouseId <= 0)
            throw Error(409, "CLOSING_COUNTER_FORBIDDEN", "Quầy/ca hiện tại đã đổi hoặc không thuộc người chốt.");
        return counter;
    }
    private async Task PrepareHandoverVouchers(DeliveryOrder order, bool createsSale, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM Orders WITH (UPDLOCK,HOLDLOCK) WHERE StoreId={Store} AND Id={order.SourceCartId}; SELECT Id FROM OrderLines WITH (UPDLOCK,HOLDLOCK) WHERE StoreId={Store} AND OrderId={order.SourceCartId}; SELECT Id FROM OrderRewardVouchers WITH (UPDLOCK,HOLDLOCK) WHERE StoreId={Store} AND OrderId={order.SourceCartId};", ct);
        var links = await db.Set<OrderRewardVoucher>().AsNoTracking().Where(x => x.StoreId == Store && x.OrderId == order.SourceCartId).OrderBy(x => x.VoucherId).ToListAsync(ct);
        foreach (var link in links)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM CustomerRewardVouchers WITH (UPDLOCK,HOLDLOCK) WHERE StoreId={Store} AND Id={link.VoucherId};", ct);
            var voucher = await db.Set<CustomerRewardVoucher>().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == link.VoucherId, ct);
            if (voucher is not null) await db.Entry(voucher).ReloadAsync(ct);
            if (voucher is null || voucher.CustomerId != order.CustomerId || voucher.Value != link.VoucherValue || voucher.UsedOrderId.HasValue || voucher.UsedAtUtc.HasValue ||
                voucher.Status != CustomerRewardVoucherStatus.Available && voucher.Status.ToString() != "ReservedForDelivery" ||
                await db.DeliveryOrders.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Id != order.Id && x.State != DeliveryState.Cancelled &&
                    !db.DeliveryJournalEntries.Any(j => j.StoreId == Store && j.DeliveryOrderId == x.Id && j.Kind == DeliveryJournalKind.Settlement && j.MoneyAmount == 0 && j.SaleOrderId == null) &&
                    db.Set<OrderRewardVoucher>().Any(v => v.StoreId == Store && v.OrderId == x.SourceCartId && v.VoucherId == link.VoucherId), ct))
                throw Error(409, "VOUCHER_UNAVAILABLE", "Voucher nguồn không còn thuộc riêng đơn giao này hoặc đã dùng.");
            // Reservation is not redemption. The real POS tail below consumes the actual sale link exactly once.
            if (voucher.Status.ToString() == "ReservedForDelivery") voucher.Status = CustomerRewardVoucherStatus.Available;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<DeliveryHandoverAck> ConfirmHandoverAsync(int id, int orderId, DeliveryHandoverRequest request, CancellationToken ct = default)
    {
        _ = DeliveryTripPolicy.Version(request.ExpectedOrderVersion);
        if (!request.PhysicalGoodsConfirmed) throw Error(400, "PHYSICAL_RETURN_REQUIRED", "Cần xác nhận người giao và hàng thực tế tại tiệm.");
        var required = DeliveryCapability.View | DeliveryCapability.Finalize | FinanceCapability(request.PaymentKind);
        var extra = DeliveryCapability.None;
        var ack = await Command(id, request.ClientRequestId, "handover", request.ExpectedTripVersion,
            new DeliveryHandoverCommandEnvelope(orderId, request), required, null, async (value, members, unusedTime) =>
            {
                var trip = value!; await DepartedProof(trip, members, ct); await Courier(trip.CourierUserId, ct);
                if (trip.CourierUserId != request.CourierUserId) throw Error(400, "COURIER_MISMATCH", "Cần đúng người giao của chuyến.");
                var member = members.SingleOrDefault(x => x.DeliveryOrderId == orderId) ?? throw Error(404, "DELIVERY_NOT_FOUND", "Đơn không thuộc chuyến.");
                var order = await Order(orderId, false, true, ct); Match(request.ExpectedOrderVersion, order.RowVersion);
                if (!Reportable(order.State) || await db.DeliveryJournalEntries.AnyAsync(x => x.StoreId == Store && x.DeliveryOrderId == orderId && x.Kind == DeliveryJournalKind.Settlement, ct))
                    throw Error(409, "HANDOVER_LOCKED", "Đơn đã chốt hoặc chưa xuất phát.");
                var wasCancellationPending = order.State == DeliveryState.CancellationPending;
                var sources = await OutcomeSources(trip, member, order, ct);
                var quantities = HandoverQuantities(sources, request.Lines);
                var prices = await HandoverPrices(member, order, quantities, ct);
                var cash = DeliveryOutcomePolicy.ParseMoney(request.ReportedCashText);
                var actualCash = DeliveryOutcomePolicy.ParseMoney(request.ActualReceivedCashText);
                var transfer = DeliveryOutcomePolicy.ParseMoney(request.ConfirmedTransferText);
                var reason = DeliveryOutcomePolicy.Validate(request.Outcome, request.Reason, quantities.Select(q => new DeliveryOutcomeQuantity(
                    q.LineId, q.Dispatched, q.Accepted, q.Dispatched - q.Accepted, q.Delivered, q.ActualReturned)).ToArray(), cash, transfer);
                var prior = await LatestOutcome(trip, member, order, sources, ct); var returned = await ReturnSnapshot(trip, ct);
                var now = await SqlUtcAtOrAfter(new[] { trip.DepartedAtUtc!.Value, prior?.RecordedAtUtc.UtcDateTime ?? DateTime.MinValue,
                    returned?.RecordedAtUtc.UtcDateTime ?? DateTime.MinValue }.Max(), ct);
                if (request.CustomerReceivedAtUtc is { } received)
                    DeliveryValues.Require(received.Offset == TimeSpan.Zero && received >= Utc(trip.DepartedAtUtc.Value) && received <= Utc(now),
                        "CUSTOMER_TIME_INVALID", "Thời gian khách nhận phải nằm từ xuất phát đến giờ ghi SQL.");
                var counter = await ClosingCounter(request, ct);
                var amount = prices.Sum(x => x.Net); var createsSale = quantities.Any(x => x.Delivered > 0);
                var bankReference = request.TransferReference?.Trim(); int? bankStore = null;
                if (request.PaymentKind == "BankTransfer")
                {
                    if (!request.TransferConfirmed || request.BankAccountId is not > 0 || !await db.Set<StoreBankAccount>().AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Id == request.BankAccountId && x.IsActive, ct))
                        throw Error(400, "BANK_UNCONFIRMED", "Cần xác nhận tiền vào tài khoản đang hoạt động cùng cửa hàng.");
                    bankStore = Store; bankReference = string.IsNullOrWhiteSpace(bankReference) ? "DELIVERY-" + request.ClientRequestId.ToString("N") : bankReference;
                }
                var customer = order.CustomerId.HasValue ? await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == order.CustomerId, ct) : null;
                var credit = request.PaymentKind == "CustomerCredit" ? amount : 0;
                var delta = quantities.Where(x => x.ActualReturned > x.Accepted).Select(x => new DeliveryInventoryReturnLine(x.LineId, Qty(x.ActualReturned - x.Accepted))).ToArray();
                if (delta.Length > 0)
                {
                    extra |= DeliveryCapability.ReceiveReturns; await Authorize(required | extra, ct);
                    await inventory.ReceiveReturnsInTransactionAsync(order.Id, new(DeliveryTripPolicy.ChildCommandId(request.ClientRequestId, order.Id),
                        Version(order.RowVersion), delta, "Bàn giao sau giao hàng tại tiệm"), ct);
                    order = await Order(orderId, false, true, ct);
                }
                // The same atomic command records an actual reconciliation phase. A failure below rolls back this phase and every return receipt.
                order.State = DeliveryHandoverPolicy.ReconciledState(order.State); order.Revision++; await db.SaveChangesAsync(ct);
                var reconciliation = JsonSerializer.Serialize(new { schema = "delivery-handover-reconciliation-v1", tripId = trip.Id,
                    deliveryOrderId = order.Id, actorUserId = Actor, recordedAtUtc = Utc(now), request.ClientRequestId,
                    wasCancellationPending, quantities, paymentKind = request.PaymentKind, reportedCashText = request.ReportedCashText,
                    actualReceivedCashText = request.ActualReceivedCashText, confirmedTransferText = request.ConfirmedTransferText }, Json);
                db.DeliveryRevisions.Add(new() { StoreId = Store, DeliveryOrderId = order.Id, Revision = order.Revision, ActorUserId = Actor,
                    Action = "handover-reconciled", AggregateVersion = Version(order.RowVersion), SnapshotJson = reconciliation, SnapshotHash = Hash(reconciliation) });
                await db.SaveChangesAsync(ct);
                var settlement = DeliverySettlementPolicy.Plan(Origin(order), new(Store, counter.TerminalId, counter.ShiftId, counter.WarehouseId, Actor, true),
                    new(Store, Actor, required | extra, new HashSet<int> { order.SourceWarehouseId }, new HashSet<int> { order.SourceLegalEntityId }),
                    order.State, quantities.Select(q => new DeliveryQuantityLine(q.LineId, q.Dispatched, q.Dispatched, q.Dispatched,
                        q.Delivered, q.ActualReturned, q.ActualReturned, 0, 0, 1, 0)).ToArray(),
                    prices.Select(p => new DeliveryApprovedPriceLine(p.DeliveryLineId, quantities.Single(q => q.LineId == p.DeliveryLineId).Dispatched,
                        p.ApprovedGross, p.ApprovedGross - p.ApprovedNet, 0, p.ApprovedNet)).ToArray(),
                    new(DeliveryHandoverPolicy.Method(request.PaymentKind), cash, actualCash, transfer, request.TransferConfirmed, bankReference, bankStore,
                        credit, customer is null ? null : new(Store, customer.Id, customer.IsActive, customer.HaveDebt)), Utc(trip.DepartedAtUtc.Value), Utc(now), Utc(now));
                if (settlement.SaleAmount != amount) throw Error(409, "PRICE_SNAPSHOT_INVALID", "Kế hoạch chốt không khớp tiền quote thực giao.");
                var pricedCost = await saleCostEvidence!.ReadDeliveredSlicesAsync(orderId, prices, ct);
                await PrepareHandoverVouchers(order, createsSale, ct);
                int? saleId = null;
                if (createsSale)
                    saleId = (await saleFinalizer!.FinalizeDeliverySaleAsync(new(Store, Actor, order.SourceCartId, order.Id,
                        order.SourceWarehouseId, order.SourceLegalEntityId, counter.TerminalId, counter.ShiftId, request.ClientRequestId,
                        request.PaymentKind, amount, actualCash, transfer, credit, request.BankAccountId, bankReference, request.CreditDueDate,
                        request.CreditNote, Utc(now), pricedCost) { SourceCustomerId = order.CustomerId }, ct)).SaleOrderId;
                order.State = wasCancellationPending && !createsSale ? DeliveryState.Cancelled : DeliveryState.Settled;
                order.Revision++; await db.SaveChangesAsync(ct);
                var result = new DeliveryHandoverResultDto(order.Id, saleId, order.State.ToString(), request.PaymentKind, Dong(amount), Dong(actualCash),
                    Dong(transfer), Dong(credit), counter.TerminalId, counter.ShiftId, order.SourceWarehouseId, order.SourceLegalEntityId, Utc(now));
                var snapshot = new DeliveryHandoverSnapshot("delivery-handover-v1", Store, trip.Id, order.Id, member.Id, member.CurrentCustodyEventId!.Value,
                    order.SourceCartId, order.SourceWarehouseId, order.SourceLegalEntityId, order.Revision, Version(order.RowVersion), request.ClientRequestId,
                    Actor, trip.CourierUserId, "AtStore", request.Outcome, reason, request.CustomerReceivedAtUtc, result, request.Lines.OrderBy(x => x.LineId).ToArray(), pricedCost)
                { TripRevision = trip.Revision + 1 };
                var json = JsonSerializer.Serialize(snapshot, Json);
                db.DeliveryRevisions.Add(new() { StoreId = Store, DeliveryOrderId = order.Id, Revision = order.Revision, ActorUserId = Actor,
                    Action = "counter-handover", AggregateVersion = Version(order.RowVersion), SnapshotJson = json, SnapshotHash = Hash(json) });
                db.DeliveryJournalEntries.Add(new() { StoreId = Store, DeliveryOrderId = order.Id, SourceWarehouseId = order.SourceWarehouseId,
                    SourceLegalEntityId = order.SourceLegalEntityId, Kind = DeliveryJournalKind.Settlement,
                    PostingKey = "handover:" + request.ClientRequestId.ToString("N"), MoneyAmount = amount, SaleOrderId = saleId });
                var eventId = Guid.NewGuid();
                db.DeliveryOutboxMessages.Add(new() { StoreId = Store, DeliveryOrderId = order.Id, Revision = order.Revision, EventId = eventId,
                    Action = "handover", PayloadJson = JsonSerializer.Serialize(new { eventId, tripId = trip.Id, deliveryOrderId = order.Id, saleOrderId = saleId, actorUserId = Actor, recordedAtUtc = Utc(now) }, Json) });
                await db.SaveChangesAsync(ct); trip.Revision++; return trip;
            }, ct, () => extra);
        var saved = await HandoverSnapshot(orderId, request.ClientRequestId, ct) ?? throw Error(409, "RECEIPT_INVALID", "Thiếu kết quả bàn giao.");
        return new(request.ClientRequestId, id, orderId, "handover", ack.AppliedRevision, ack.AppliedVersion, saved.OrderRevision,
            saved.OrderVersion, saved.Settlement.State, saved.Settlement.RecordedAtUtc, ack.Replayed, saved.Settlement);
    }

    private async Task<DeliveryHandoverSnapshot?> HandoverSnapshot(int orderId, Guid? key, CancellationToken ct)
    {
        var revisions = await db.DeliveryRevisions.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == orderId && x.Action == "counter-handover")
            .OrderByDescending(x => x.Revision).ToListAsync(ct);
        foreach (var revision in revisions)
        {
            var saved = ReadSnapshot<DeliveryHandoverSnapshot>(revision.SnapshotJson, revision.SnapshotHash);
            if (key.HasValue && saved.ClientRequestId != key) continue;
            Proof(saved.Schema == "delivery-handover-v1" && saved.StoreId == Store && saved.DeliveryOrderId == orderId && saved.OrderRevision == revision.Revision &&
                saved.OrderVersion == revision.AggregateVersion && saved.ActorUserId == revision.ActorUserId && saved.ClientRequestId != Guid.Empty &&
                saved.ReportSource == "AtStore" && saved.Settlement.DeliveryOrderId == orderId && saved.Settlement.SourceWarehouseId == saved.SourceWarehouseId &&
                saved.Settlement.SourceLegalEntityId == saved.SourceLegalEntityId);
            var journal = await db.DeliveryJournalEntries.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.DeliveryOrderId == orderId && x.Kind == DeliveryJournalKind.Settlement, ct);
            Proof(journal is not null && journal.SaleOrderId == saved.Settlement.SaleOrderId && journal.MoneyAmount == DeliveryOutcomePolicy.ParseMoney(saved.Settlement.PayableText) &&
                journal.SourceWarehouseId == saved.SourceWarehouseId && journal.SourceLegalEntityId == saved.SourceLegalEntityId && journal.PostingKey == "handover:" + saved.ClientRequestId.ToString("N"));
            var order = await Order(orderId, false, false, ct);
            Proof(saved.SourceOrderId == order.SourceCartId && saved.SourceWarehouseId == order.SourceWarehouseId &&
                saved.SourceLegalEntityId == order.SourceLegalEntityId && saved.OrderRevision <= order.Revision && saved.SaleLines is not null && saved.Lines is not null &&
                saved.SaleLines.Select(x => x.DeliveryLineId).SequenceEqual(saved.Lines.Select(x => x.LineId)) &&
                saved.SaleLines.Sum(x => x.Net) == journal!.MoneyAmount && saved.Settlement.State is "Settled" or "Cancelled");
            var root = await db.DeliveryTripCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.ClientRequestId == saved.ClientRequestId, ct);
            Proof(root is not null && root.TripId == saved.TripId && root.ActorUserId == saved.ActorUserId && root.Operation == "handover");
            var ack = JsonSerializer.Deserialize<DeliveryTripCommandAck>(root!.OutcomeJson, Json);
            Proof(ack is not null && ack.AppliedRevision == saved.TripRevision && ack.Orders.Any(x => x.DeliveryOrderId == orderId &&
                x.Revision == saved.OrderRevision && x.Version == saved.OrderVersion && x.State == saved.Settlement.State));
            if (saved.Settlement.SaleOrderId is int saleId)
            {
                var sale = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == saleId, ct);
                Proof(sale is not null && sale.Status == OrderStatus.Completed && sale.POSShiftId == saved.Settlement.ShiftId &&
                    sale.CustomerId == order.CustomerId && sale.GrandTotal == journal!.MoneyAmount &&
                    sale.PaidTotal == DeliveryOutcomePolicy.ParseMoney(saved.Settlement.ReceivedCashText) + DeliveryOutcomePolicy.ParseMoney(saved.Settlement.ConfirmedTransferText) &&
                    sale.BalanceDue == DeliveryOutcomePolicy.ParseMoney(saved.Settlement.CustomerDebtText));
                var allocations = await db.Set<OrderLegalEntityAllocation>().AsNoTracking().Where(x => x.StoreId == Store && x.OrderId == saleId).ToListAsync(ct);
                Proof(allocations.Count > 0 && allocations.Sum(x => x.NetAmount) == sale!.GrandTotal && allocations.All(x =>
                    x.WarehouseId == order.SourceWarehouseId && x.LegalEntityId == order.SourceLegalEntityId && x.InventoryTransactionId.HasValue));
            }
            else Proof(saved.SaleLines.All(x => x.DeliveredBaseQuantity == 0) && journal!.MoneyAmount == 0);
            return saved;
        }
        return null;
    }
    private async Task ValidateHandoverReplay(DeliveryTripCommandReceipt receipt, DeliveryTripCommandAck ack,
        DeliveryHandoverCommandEnvelope envelope, CancellationToken ct)
    {
        var counter = await ClosingCounter(envelope.Request, ct);
        var saved = await HandoverSnapshot(envelope.OrderId, envelope.Request.ClientRequestId, ct) ?? throw Error(409, "RECEIPT_INVALID", "Thiếu chứng cứ chốt hàng.");
        var order = await Order(envelope.OrderId, false, false, ct);
        var trip = await Trip(receipt.TripId, false, ct); var members = await Members(trip.Id, false, ct); await DepartedProof(trip, members, ct);
        var member = members.SingleOrDefault(x => x.DeliveryOrderId == order.Id);
        Proof(member is not null && member.Id == saved.TripOrderId && member.CurrentCustodyEventId == saved.CustodyEventId);
        var sources = await OutcomeSources(trip, member!, order, ct);
        var quantities = HandoverQuantities(sources, saved.Lines);
        Proof(quantities.All(q => q.ActualReturned == q.Accepted && saved.SaleLines.Single(x => x.DeliveryLineId == q.LineId).DeliveredBaseQuantity == q.Delivered));
        var prices = await HandoverPrices(member!, order, quantities, ct);
        Proof(prices.Sum(x => x.Net) == DeliveryOutcomePolicy.ParseMoney(saved.Settlement.PayableText) && prices.All(x =>
            saved.SaleLines.Any(p => p.DeliveryLineId == x.DeliveryLineId && p.VariantId == x.VariantId && p.Net == x.Net && p.Gross == x.Gross && p.SourceOrderLineId == x.SourceOrderLineId)));
        Proof(saved.TripId == trip.Id && saved.ActorUserId == Actor && saved.CourierUserId == trip.CourierUserId && saved.CourierUserId == envelope.Request.CourierUserId &&
            saved.Settlement.TerminalId == counter.TerminalId && saved.Settlement.ShiftId == counter.ShiftId &&
            saved.SourceOrderId == order.SourceCartId && saved.SourceWarehouseId == order.SourceWarehouseId && saved.SourceLegalEntityId == order.SourceLegalEntityId &&
            ack.ClientRequestId == saved.ClientRequestId && ack.TripId == trip.Id && ack.Operation == "handover" &&
            ack.Orders.Any(x => x.DeliveryOrderId == order.Id && x.Revision == saved.OrderRevision && x.Version == saved.OrderVersion && x.State == saved.Settlement.State));
        var revision = await db.DeliveryTripRevisions.AsNoTracking().SingleAsync(x => x.StoreId == Store && x.TripId == trip.Id && x.Revision == ack.AppliedRevision, ct);
        Proof(Hash(revision.SnapshotJson) == revision.SnapshotHash && revision.Action == "handover" && revision.AggregateVersion == ack.AppliedVersion);
        using var document = JsonDocument.Parse(revision.SnapshotJson);
        var original = document.RootElement.GetProperty("request").Deserialize<DeliveryHandoverCommandEnvelope>(Json);
        Proof(original is not null && ReceiptRequestHash(trip, receipt, original) == receipt.RequestHash);
    }
    private async Task<DeliveryTripDetailDto> ProjectHandoverTrip(DeliveryTrip trip, DeliveryTripDetailDto detail, CancellationToken ct)
    {
        DeliveryHandoverSnapshot? first = null; var completed = true; var pending = false;
        var time = detail.ServerTimeUtc; var rows = new List<DeliveryTripOrderDto>();
        foreach (var row in detail.Orders.Where(x => x.IsActive))
        {
            var saved = await HandoverSnapshot(row.DeliveryOrderId, null, ct);
            if (saved is null) { completed &= row.Outcome is { IsCurrentValid: true }; pending = true; rows.Add(row); continue; }
            if (first is null || first.Settlement.RecordedAtUtc > saved.Settlement.RecordedAtUtc) first = saved;
            if (time < saved.Settlement.RecordedAtUtc) time = saved.Settlement.RecordedAtUtc;
            rows.Add(row with { Lines = row.Lines.Select(x => x with { InCustodyBaseQuantityText = "0" }).ToArray() });
        }
        if (first is null) return detail;
        var arrival = detail.ReturnToStore ?? new DeliveryTripReturnDto(first.TripRevision, first.ActorUserId,
            await Name(first.ActorUserId, ct), first.CourierUserId, await Name(first.CourierUserId, ct), first.Settlement.RecordedAtUtc,
            "Bàn giao sau giao hàng tại tiệm");
        rows.AddRange(detail.Orders.Where(x => !x.IsActive));
        return detail with { ReturnToStore = arrival, AllOutcomesComplete = completed,
            ReadyForReconciliation = completed && pending, ServerTimeUtc = time, Orders = rows.OrderBy(x => x.Position).ToArray() };
    }
}
