using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.POSShifts;

/// <summary>Read-only, whole-shift evidence. Never changes cash counts, payments or approval state.</summary>
public sealed class POSShiftReconciliationService(AppDbContext db, ICurrentStore store, ICurrentUser user,
    IStoreAdminAccess admin) : IPOSShiftReconciliationService
{
    public async Task<POSShiftReconciliationDto> GetAsync(int? shiftId, CancellationToken ct = default)
    {
        var storeId = store.StoreId;
        if (storeId <= 0 || !user.IsAuthenticated || user.UserId is not > 0)
            throw new ForbiddenAppException("Vui lòng đăng nhập và chọn đúng cửa hàng.");
        var shifts = db.POSShifts.AsNoTracking().Where(x => x.StoreId == storeId);
        var shift = shiftId.HasValue
            ? await shifts.SingleOrDefaultAsync(x => x.Id == shiftId.Value, ct)
            : await shifts.SingleOrDefaultAsync(x => x.TerminalId == user.TerminalId && x.Status == POSShiftStatus.Open, ct);
        if (shift == null) throw new NotFoundAppException("Không tìm thấy ca cần đối soát. Hãy mở ca hoặc chọn ca từ lịch sử.");
        if (shift.OpenedByUserId != user.UserId && !await admin.IsAdminAsync(ct))
            throw new ForbiddenAppException("Bạn chỉ được đối soát ca của mình.");

        // Explicit store predicates remain mandatory on every IgnoreQueryFilters query.
        var orders = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == storeId && x.POSShiftId == shift.Id)
            .Include(x => x.Customer)
            .Include(x => x.Payments.Where(p => p.StoreId == storeId))
            .Include(x => x.Lines.Where(l => l.StoreId == storeId && !l.IsDeleted))
            .AsSplitQuery().OrderByDescending(x => x.Id).ToListAsync(ct);
        var cash = await db.POSShiftCashTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == storeId && x.POSShiftId == shift.Id).OrderByDescending(x => x.Id).ToListAsync(ct);
        // A refund belongs to the shift that paid it out, even when its original sale was in another shift.
        var refunds = await db.SalesReturns.AsNoTracking().Where(x => x.StoreId == storeId && x.POSShiftId == shift.Id)
            .Include(x => x.Payments.Where(p => p.StoreId == storeId)).AsSplitQuery()
            .OrderByDescending(x => x.Id).ToListAsync(ct);
        var deposits = await db.Set<CustomerDepositEntry>().AsNoTracking()
            .Where(x => x.StoreId == storeId && x.POSShiftId == shift.Id).OrderByDescending(x => x.Id).ToListAsync(ct);
        var debts = await db.Set<CustomerDebtReceipt>().AsNoTracking()
            .Where(x => x.StoreId == storeId && x.POSShiftId == shift.Id)
            .Include(x => x.Entries.Where(e => e.StoreId == storeId && e.Kind == "Collection"))
            .AsSplitQuery().OrderByDescending(x => x.Id).ToListAsync(ct);
        var depositIds = deposits.Select(x => x.CustomerDepositId).Distinct().ToArray();
        var depositCustomers = await db.Set<CustomerDeposit>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == storeId && depositIds.Contains(x.Id))
            .Select(x => new { x.Id, x.CustomerId }).ToDictionaryAsync(x => x.Id, x => x.CustomerId, ct);
        var customerIds = depositCustomers.Values.Concat(debts.Select(x => x.CustomerId)).Distinct().ToArray();
        var customerNames = await db.Customers.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == storeId && customerIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var allocatedIds = debts.SelectMany(x => x.Entries).Select(x => x.OrderId).Distinct().ToArray();
        var allocatedOrders = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == storeId && allocatedIds.Contains(x.Id))
            .Select(x => new { x.Id, x.OrderNumber }).ToDictionaryAsync(x => x.Id, x => x.OrderNumber, ct);
        string CustomerName(int id) => customerNames.GetValueOrDefault(id) ?? (id > 0 ? $"Khách #{id}" : "Chưa lưu thông tin khách");
        var qrs = await db.PosPaymentQrRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == storeId && db.Orders.Any(o => o.StoreId == storeId && o.Id == x.OrderId && o.POSShiftId == shift.Id))
            .OrderByDescending(x => x.Id).Select(x => new PosPaymentQrRequest {
                Id = x.Id, OrderId = x.OrderId, Amount = x.Amount, RequestCode = x.RequestCode,
                PaymentId = x.PaymentId, ConfirmMode = x.ConfirmMode, Status = x.Status, CreatedAtUtc = x.CreatedAtUtc
            }).ToListAsync(ct);
        var sessions = await db.Set<AcbQrSession>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == storeId && x.ShiftId == shift.Id).Select(x => new AcbQrSession {
                QrRequestId = x.QrRequestId, OrderId = x.OrderId, PaymentId = x.PaymentId, Amount = x.Amount, ProviderOrderId = x.ProviderOrderId,
                Status = x.Status, ConfirmationSource = x.ConfirmationSource, ReviewReason = x.ReviewReason
            }).ToListAsync(ct);
        var adjustments = await db.Set<POSCashAdjustmentRequest>().AsNoTracking()
            .Where(x => x.StoreId == storeId && x.POSShiftId == shift.Id).OrderByDescending(x => x.Id).ToListAsync(ct);
        var paymentAdjustments = await db.Set<POSPaymentAdjustmentRequest>().AsNoTracking()
            .Where(x => x.StoreId == storeId && x.POSShiftId == shift.Id).OrderByDescending(x => x.Id).ToListAsync(ct);
        var denominations = await db.POSShiftCashDenominations.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.POSShiftId == shift.Id).ToListAsync(ct);
        var actorIds = orders.Select(x => x.CreatedBy).Concat(orders.SelectMany(x => x.Payments).Select(x => x.CreatedBy))
            .Concat(cash.Select(x => (int?)x.CreatedByUserId)).Concat(refunds.SelectMany(x => x.Payments).Select(x => x.CreatedBy))
            .Concat(deposits.Select(x => x.CreatedBy)).Concat(debts.Select(x => x.CreatedBy)).Append(shift.OpenedByUserId)
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var names = await db.Users.AsNoTracking().Where(x => actorIds.Contains(x.Id))
            .Select(x => new { x.Id, Name = x.FullName ?? x.UserName }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        string Actor(int? id) => id.HasValue ? names.GetValueOrDefault(id.Value) ?? $"Nhân viên #{id}" : "Chưa lưu người ghi nhận";

        ReconciliationPaymentDto Payment(OrderPayment p)
        {
            var session = sessions.FirstOrDefault(x => x.PaymentId == p.Id);
            var qr = qrs.FirstOrDefault(x => x.PaymentId == p.Id);
            var pending = paymentAdjustments.FirstOrDefault(x => x.OrderId == p.OrderId && x.Status == POSCashAdjustmentStatus.Pending);
            var order = orders.First(x => x.Id == p.OrderId);
            var protectedBank = sessions.Any(x => x.PaymentId == p.Id && x.ConfirmationSource != AcbConfirmationSource.OfflineManual)
                || qrs.Any(x => x.PaymentId == p.Id && x.ConfirmMode != BankQrConfirmMode.Manual);
            var canRequest = shift.OpenedByUserId == user.UserId && !order.IsDeleted && order.Status is OrderStatus.Completed or OrderStatus.Refunded
                && !p.IsDeleted && !p.IsDebtCollection && p.Amount > 0 && !protectedBank && pending == null;
            var verified = !p.IsDeleted && p.Method == PaymentMethod.BankTransfer && session != null &&
                session.ConfirmationSource.HasValue && session.ConfirmationSource != AcbConfirmationSource.OfflineManual &&
                session.Status is AcbSessionStatus.Received or AcbSessionStatus.Completed &&
                session.OrderId == p.OrderId && session.Amount == p.Amount && session.ProviderOrderId == p.ReferenceCode;
            var confirmation = p.IsDeleted ? "Khoản thanh toán đã hủy" : p.Method != PaymentMethod.BankTransfer ? "Đã ghi nhận" : verified ? "ACB đã xác nhận" :
                session?.Status == AcbSessionStatus.ReviewRequired ? "ACB cần kiểm tra" :
                session?.ConfirmationSource == AcbConfirmationSource.OfflineManual ? "Xác nhận thủ công khi mất kết nối" :
                session != null && session.ConfirmationSource == null ? "Chưa lưu nguồn xác nhận ACB" :
                session != null ? "Thông tin ACB chưa khớp khoản thanh toán" :
                qr?.ConfirmMode == BankQrConfirmMode.Manual ? "Nhân viên xác nhận QR thủ công" :
                qr != null ? "Chưa có xác nhận ACB liên kết khoản thanh toán" : "Nhân viên ghi nhận chuyển khoản";
            return new(p.Id, p.Method.ToString(), p.Amount, p.ReferenceCode, p.Provider, p.PaidAtUtc,
                Actor(p.CreatedBy), p.IsDeleted, p.IsDebtCollection, confirmation, verified, canRequest,
                shift.OpenedByUserId == user.UserId ? pending?.Id : null);
        }
        var orderRows = orders.Where(x => x.Lines.Count > 0 || x.Payments.Count > 0 || x.Status != OrderStatus.Draft)
            .Select(o =>
            {
                var active = o.Payments.Where(p => !p.IsDeleted && !p.IsDebtCollection).ToList();
                var appliedCash = 0m; var appliedNonCash = 0m;
                if (!o.IsDeleted && o.Status is OrderStatus.Completed or OrderStatus.Refunded)
                {
                    var remaining = Math.Max(0, o.GrandTotal - o.DepositAmount);
                    foreach (var p in active.Where(p => p.Amount > 0).OrderBy(p => p.Method == PaymentMethod.Cash ? 2 : 1).ThenBy(p => p.Id))
                    {
                        var amount = Math.Min(p.Amount, remaining);
                        if (p.Method == PaymentMethod.Cash) appliedCash += amount; else appliedNonCash += amount;
                        remaining -= amount;
                    }
                }
                var paid = active.Sum(p => p.Amount) + o.DepositAmount;
                return new ReconciliationOrderDto
                {
                    Id = o.Id, Number = o.OrderNumber ?? $"Đơn #{o.Id}", Status = o.Status.ToString(),
                    Customer = o.Customer?.StoreId == storeId ? o.Customer.Name : "Khách lẻ", Actor = Actor(o.CreatedBy),
                    CreatedAtUtc = o.CreatedAtUtc, CompletedAtUtc = o.CompletedAtUtc, Cancelled = o.IsDeleted,
                    GrandTotal = o.GrandTotal, Deposit = o.DepositAmount, Note = o.Note, IsCreditSale = o.IsCreditSale,
                    CashReceived = active.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount),
                    BankReceived = active.Where(p => p.Method == PaymentMethod.BankTransfer).Sum(p => p.Amount),
                    OtherReceived = active.Where(p => p.Method is not (PaymentMethod.Cash or PaymentMethod.BankTransfer)).Sum(p => p.Amount),
                    Remaining = Math.Max(0, o.GrandTotal - paid), Surplus = Math.Max(0, paid - o.GrandTotal),
                    CashApplied = appliedCash, NonCashApplied = appliedNonCash,
                    Payments = o.Payments.OrderBy(p => p.Id).Select(Payment).ToList(),
                    Lines = o.Lines.Select(l => new ReconciliationLineDto(l.ItemName ?? "Sản phẩm", l.Quantity, l.UnitPrice, l.LineTotal)).ToList()
                };
            }).ToList();
        var expected = shift.OpeningCash + shift.CashSalesTotal + shift.CashInTotal - shift.CashOutTotal - shift.CashRefundTotal;
        var cashRefund = refunds.Where(r => r.Status == SalesReturnStatus.Completed)
            .SelectMany(r => r.Payments).Where(p => !p.IsDeleted && p.Method == PaymentMethod.Cash).Sum(p => p.Amount);
        var detailExpected = shift.OpeningCash + orderRows.Sum(o => o.CashApplied) +
            cash.Where(x => !x.IsDeleted).Sum(x => x.Type == POSShiftCashTransactionType.CashIn ? x.Amount : -x.Amount) - cashRefund;
        return new()
        {
            StoreId = storeId, ShiftId = shift.Id, ShiftCode = shift.ShiftCode ?? $"Ca #{shift.Id}",
            OwnerId = shift.OpenedByUserId, OwnerName = Actor(shift.OpenedByUserId), TerminalId = shift.TerminalId,
            TerminalName = await db.POSTerminals.Where(x => x.StoreId == storeId && x.Id == shift.TerminalId).Select(x => x.Name).SingleOrDefaultAsync(ct) ?? $"Quầy #{shift.TerminalId}",
            Status = shift.Status.ToString(), OpenedAtUtc = shift.OpenedAtUtc, ClosedAtUtc = shift.ClosedAtUtc,
            CanReturnToClose = shift.Status == POSShiftStatus.Open && shift.TerminalId == user.TerminalId && shift.OpenedByUserId == user.UserId,
            AsOfUtc = DateTime.UtcNow, OpeningCash = shift.OpeningCash, OpenNote = shift.OpenNote,
            CashSales = shift.CashSalesTotal, NonCashSales = shift.NonCashSalesTotal,
            CashIn = shift.CashInTotal, CashOut = shift.CashOutTotal, CashRefunds = shift.CashRefundTotal,
            NonCashRefunds = shift.NonCashRefundTotal, ExpectedCash = expected, DetailExpectedCash = detailExpected,
            ActualCash = shift.ClosingCashActual, ReceivedCash = shift.CashReceivedAmount, NeedsReconciliation = shift.NeedsCashReconciliation,
            Orders = orderRows,
            CashTransactions = cash.Select(x => new ReconciliationCashDto(x.Id, x.Type.ToString(), x.Amount, x.Reason, x.Note,
                x.CreatedAtUtc, Actor(x.CreatedByUserId), x.IsDeleted, x.CreatedByUserId == user.UserId && !x.IsDeleted && !POSCashAdjustmentService.DepositVoucher(x),
                adjustments.FirstOrDefault(a => a.TransactionId == x.Id && a.Status == POSCashAdjustmentStatus.Pending)?.Id, x.CustomerDepositEntryId)).ToList(),
            Refunds = refunds.Select(x => new ReconciliationRefundDto(x.Id, x.OrderId, x.ReturnNumber, x.Status.ToString(), x.Reason,
                x.Note, x.CompletedAtUtc ?? x.CreatedAtUtc, x.RefundTotal, x.DepositRestoredTotal,
                x.Payments.Select(p => new ReconciliationPaymentDto(p.Id, p.Method.ToString(), p.Amount, p.ReferenceCode, p.Provider,
                    p.PaidAtUtc, Actor(p.CreatedBy), p.IsDeleted, false, "Khoản hoàn đã ghi nhận", false)).ToList())).ToList(),
            OtherMovements = deposits.Select(x => new ReconciliationMovementDto(x.Id, "Deposit:" + x.Kind, x.Amount,
                x.Method?.ToString(), x.Reference, x.Note, x.OrderId, x.CreatedAtUtc, Actor(x.CreatedBy),
                depositCustomers.GetValueOrDefault(x.CustomerDepositId), CustomerName(depositCustomers.GetValueOrDefault(x.CustomerDepositId)), [],
                shift.OpenedByUserId == user.UserId && x.Kind == "Receive" && x.Amount > 0 && x.Method is PaymentMethod.Cash or PaymentMethod.BankTransfer &&
                    !paymentAdjustments.Any(a => a.DepositEntryId == x.Id && a.Status == POSCashAdjustmentStatus.Pending),
                shift.OpenedByUserId == user.UserId ? paymentAdjustments.FirstOrDefault(a => a.DepositEntryId == x.Id && a.Status == POSCashAdjustmentStatus.Pending)?.Id : null))
                .Concat(debts.Select(x => new ReconciliationMovementDto(x.Id, "DebtCollection", x.Amount, x.Method.ToString(),
                    x.Reference, x.Note, null, x.CreatedAtUtc, Actor(x.CreatedBy), x.CustomerId, CustomerName(x.CustomerId),
                    x.Entries.Where(e => allocatedOrders.ContainsKey(e.OrderId)).Select(e => new ReconciliationDebtAllocationDto(
                        e.OrderId, allocatedOrders[e.OrderId] ?? $"Đơn #{e.OrderId}", Math.Abs(e.Amount))).ToList())))
                .OrderByDescending(x => x.CreatedAtUtc).ToList(),
            Qrs = qrs.Select(x =>
            {
                var session = sessions.FirstOrDefault(s => s.QrRequestId == x.Id);
                return new ReconciliationQrDto(x.Id, x.OrderId, x.Amount, x.RequestCode, session?.Status.ToString() ?? x.Status.ToString(),
                    x.ConfirmMode == BankQrConfirmMode.Manual ? "QR xác nhận thủ công" : "QR ACB", session?.ReviewReason, x.CreatedAtUtc, x.PaymentId);
            }).ToList(),
            Adjustments = adjustments.Select(x => new ReconciliationAdjustmentDto(x.Id, x.TransactionId, x.Status.ToString(), x.IsCancellation,
                x.OldType.ToString(), x.OldAmount, x.NewType.ToString(), x.NewAmount,
                (x.IsCancellation ? 0 : Signed(x.NewType, x.NewAmount)) - Signed(x.OldType, x.OldAmount),
                x.RequestReason, x.RequestedByName, x.CreatedAtUtc, x.ReviewedByName, x.ReviewedAtUtc, x.ReviewNote)).ToList(),
            Denominations = denominations.Select(x => new ReconciliationDenominationDto(x.EntryType.ToString(), x.DenominationValue, x.Quantity, x.Amount)).ToList(),
            PaymentAdjustments = paymentAdjustments.Select(x => new ReconciliationPaymentAdjustmentDto(x.Id,x.OrderId??0,x.PaymentId??0,x.Status.ToString(),
                x.OldMethod.ToString(),x.NewMethod.ToString(),x.Amount,x.OldReference,x.NewReference,x.ExpectedDelta,x.RequestReason,x.RequestedByName,
                x.CreatedAtUtc,x.ReviewedByName,x.ReviewedAtUtc,x.ReviewNote,x.ReconciledAtUtc,x.DepositEntryId)).ToList()
        };
    }
    private static decimal Signed(POSShiftCashTransactionType type, decimal amount) => type == POSShiftCashTransactionType.CashIn ? amount : -amount;
}
