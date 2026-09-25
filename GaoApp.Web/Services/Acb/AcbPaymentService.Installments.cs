using GaoApp.Application.DTOs.POS;
using GaoApp.Application.DTOs.POSPaymentQrs;
using GaoApp.Application.Interfaces.Services.POSPaymentQrs;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

public sealed partial class AcbPaymentService
{
    public async Task CancelSavedQrAsync(int qrId, CancellationToken ct)
    {
        var hint = await db.PosPaymentQrRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == qrId && x.StoreId == StoreId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy QR thanh toán.");
        await using var gate = await locks.AcquireAsync(db, hint.OrderId, ct);
        if (await CancelAsync(qrId, ct)) return;
        var qr = await db.PosPaymentQrRequests.SingleAsync(x => x.Id == qrId && x.StoreId == StoreId, ct);
        RequireTerminal(await OrderAsync(qr.OrderId, ct));
        if (qr.ConfirmMode != BankQrConfirmMode.Manual || qr.PaymentId != null ||
            qr.Status is PosPaymentQrStatus.Paid or PosPaymentQrStatus.ManualConfirmed)
            throw new InvalidOperationException("QR đã nhận tiền hoặc phải hủy qua ngân hàng.");
        qr.Status = PosPaymentQrStatus.Cancelled;
        await db.SaveChangesAsync(ct);
    }

    public async Task<POSPaymentQrDto> CreateQrAsync(OrderDraftDto draft, CreatePOSPaymentQrRequest request,
        IPOSPaymentQrService manual, CancellationToken ct)
    {
        await using var gate = await locks.AcquireAsync(db, draft.OrderId, ct);
        var order = await OrderAsync(draft.OrderId, ct);
        RequireTerminal(order);
        if (request.ClientRequestId is Guid key)
        {
            var saved = await db.PosPaymentQrRequests.SingleOrDefaultAsync(x => x.StoreId == StoreId &&
                x.OrderId == order.Id && x.ClientRequestId == key, ct);
            if (saved != null)
            {
                if (request.Amount is > 0 && request.Amount != saved.Amount)
                    throw new InvalidOperationException("Mã yêu cầu tạo QR đã được dùng với số tiền khác.");
                if (saved.Status == PosPaymentQrStatus.Cancelled)
                {
                    // A cancelled attempt is final. Retain its history and let the same browser
                    // operation key point to its replacement, including retries after token failure.
                    saved.ClientRequestId = null;
                    await db.SaveChangesAsync(ct);
                }
                else return (await ReopenQrAsync(order.Id, saved.Id, ct)).Qr;
            }
        }
        if (order.Status != OrderStatus.Draft) throw new InvalidOperationException("Đơn không còn đang thanh toán.");
        draft.BalanceDue = Math.Max(0, AcbPaymentPolicy.Balance(order));
        // The explicit creation key distinguishes a new installment from a retry or reopening.
        request.ClientRequestId ??= Guid.NewGuid();
        return await TryCreateAsync(order.Id, request, ct) ?? await manual.CreateLocalManualQrAsync(draft, request, ct);
    }

    public async Task<object> ConfirmManualQrAsync(int qrId, CancellationToken ct)
    {
        var hint = await db.PosPaymentQrRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == qrId && x.StoreId == StoreId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy QR thanh toán.");
        await using var gate = await locks.AcquireAsync(db, hint.OrderId, ct);
        var qr = await db.PosPaymentQrRequests.Include(x => x.BankAccount).SingleAsync(x => x.Id == qrId && x.StoreId == StoreId, ct);
        var order = await OrderAsync(qr.OrderId, ct);
        RequireTerminal(order);
        if (qr.ConfirmMode != BankQrConfirmMode.Manual || qr.QrRenderMode != BankQrRenderMode.LocalEmvQr)
            throw new InvalidOperationException("QR tự động phải được ACB xác nhận.");
        if (qr.PaymentId.HasValue)
        {
            if (!order.Payments.Any(x => x.Id == qr.PaymentId && !x.IsDeleted && x.Amount == qr.Amount && x.ReferenceCode == qr.RequestCode))
                throw new InvalidOperationException("Khoản thanh toán của QR đã thay đổi. Hãy kiểm tra lịch sử thanh toán.");
        }
        else
        {
            if (qr.Status != PosPaymentQrStatus.Pending || order.Status != OrderStatus.Draft)
                throw new InvalidOperationException("QR đã kết thúc, không thể ghi nhận thêm tiền.");
            if (qr.Amount <= 0 || qr.Amount > AcbPaymentPolicy.Balance(order))
                throw new InvalidOperationException("Số tiền QR vượt số còn thiếu. Hãy đối chiếu tiền thực nhận trước khi xử lý.");
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var payment = new OrderPayment { StoreId = StoreId, OrderId = order.Id, Method = PaymentMethod.BankTransfer,
                Amount = qr.Amount, ReferenceCode = qr.RequestCode, Provider = qr.BankAccount.BankCode };
            db.OrderPayments.Add(payment);
            await db.SaveChangesAsync(ct);
            qr.PaymentId = payment.Id;
            qr.Status = PosPaymentQrStatus.ManualConfirmed;
            qr.PaidAtUtc = qr.ManualConfirmedAtUtc = DateTime.UtcNow;
            qr.ManualConfirmedByUserId = runtime.UserId;
            order.PaidTotal = order.Payments.Where(x => !x.IsDeleted).Sum(x => x.Amount);
            order.BalanceDue = Math.Max(0, order.GrandTotal - order.PaidTotal);
            order.PaymentStatus = order.BalanceDue > 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Paid;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await FinishQrPaymentAsync(order, qrId, qr.Amount, ct);
    }

    private async Task<object> FinishQrPaymentAsync(Order order, int qrId, decimal amount, CancellationToken ct)
    {
        var balance = Math.Max(0, AcbPaymentPolicy.Balance(order));
        var alreadyFinalized = order.Status == OrderStatus.Completed;
        var print = false;
        if (balance == 0)
        {
            if (order.Status != OrderStatus.Completed)
            {
                // Close unused requests only after fresh bank retrieval; received or ambiguous money blocks cancellation.
                var unused = await Sessions.Where(x => x.OrderId == order.Id && x.PaymentId == null &&
                    x.Status != AcbSessionStatus.Cancelled).ToListAsync(ct);
                foreach (var session in unused) await CancelAsync(session.QrRequestId, ct);
                await pos.FinalizeAsync(order.Id, ct);
            }
            var sessions = await Sessions.Where(x => x.OrderId == order.Id && x.PaymentId != null &&
                x.Status == AcbSessionStatus.Received).ToListAsync(ct);
            foreach (var session in sessions) session.Status = AcbSessionStatus.Completed;
            var alreadyClaimed = await Sessions.AnyAsync(x => x.OrderId == order.Id && x.PrintClaimedAtUtc != null, ct) ||
                await db.PosPaymentQrRequests.AnyAsync(x => x.StoreId == StoreId && x.OrderId == order.Id && x.PrintClaimedAtUtc != null, ct);
            // A cash/card checkout may already have finalized and printed this order.
            print = !alreadyClaimed && !alreadyFinalized;
            var qr = await db.PosPaymentQrRequests.SingleAsync(x => x.Id == qrId && x.StoreId == StoreId, ct);
            if (print) qr.PrintClaimedAtUtc = DateTime.UtcNow;
            // All installments share the one final order print claim.
            foreach (var session in sessions) session.PrintClaimedAtUtc ??= DateTime.UtcNow;
            var manualUnused = await db.PosPaymentQrRequests.Where(x => x.StoreId == StoreId && x.OrderId == order.Id &&
                x.ConfirmMode == BankQrConfirmMode.Manual && x.Status == PosPaymentQrStatus.Pending).ToListAsync(ct);
            foreach (var item in manualUnused) item.Status = PosPaymentQrStatus.Cancelled;
            await db.SaveChangesAsync(ct);
            await notifier.NotifyStoreAsync(StoreId, "order_finalized", order.POSShift.TerminalId.ToString(), order.Id, heldChanged: true, ct: ct);
        }
        else await notifier.NotifyTerminalAsync(StoreId, order.POSShift.TerminalId.ToString(), "qr_payment_recorded", order.Id,
            paymentsChanged: true, message: "Đã ghi nhận một khoản chuyển khoản. Đơn còn thiếu tiền.", ct: ct);
        return new { orderId = order.Id, qrId, finalized = balance == 0, paidAmount = amount,
            paidTotal = order.Payments.Where(x => !x.IsDeleted).Sum(x => x.Amount), remainingAmount = balance,
            printUrl = print ? $"/admin/pos/orders/{order.Id}/print?autoPrint=true" : null };
    }
}
