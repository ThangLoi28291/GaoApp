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
                // Another request may have confirmed or cancelled a QR already tracked by this scope.
                await db.Entry(saved).ReloadAsync(ct);
                if (request.Amount is > 0 && request.Amount != saved.Amount)
                    throw new InvalidOperationException("Mã yêu cầu tạo QR đã được dùng với số tiền khác.");
                // A retry may reopen its own QR, but must never hide another unresolved attempt.
                await RequirePreviousQrResolvedAsync(order, ct, saved.Id);
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
        await RequirePreviousQrResolvedAsync(order, ct);
        if (order.Status != OrderStatus.Draft) throw new InvalidOperationException("Đơn không còn đang thanh toán.");
        draft.BalanceDue = Math.Max(0, AcbPaymentPolicy.Balance(order));
        // The explicit creation key distinguishes a new installment from a retry or reopening.
        request.ClientRequestId ??= Guid.NewGuid();
        return await TryCreateAsync(order.Id, request, ct) ?? await manual.CreateLocalManualQrAsync(draft, request, ct);
    }

    // Called while holding the store/order lock, before issuing any new bank or manual QR.
    private async Task RequirePreviousQrResolvedAsync(Order order, CancellationToken ct, int? retryQrId = null)
    {
        var pending = await db.PosPaymentQrRequests.AsNoTracking()
            .Where(q => q.StoreId == StoreId && q.OrderId == order.Id &&
                (!retryQrId.HasValue || q.Id != retryQrId.Value) &&
                (Sessions.Any(s => s.QrRequestId == q.Id &&
                    (s.Status == AcbSessionStatus.Creating || s.Status == AcbSessionStatus.Pending ||
                     s.Status == AcbSessionStatus.ReviewRequired ||
                     s.Status == AcbSessionStatus.Received && s.PaymentId == null)) ||
                 !Sessions.Any(s => s.QrRequestId == q.Id) && q.PaymentId == null &&
                 q.Status != PosPaymentQrStatus.Cancelled && q.Status != PosPaymentQrStatus.Paid &&
                 q.Status != PosPaymentQrStatus.ManualConfirmed))
            .OrderBy(q => q.Id).FirstOrDefaultAsync(ct);
        if (pending == null) return;

        var message = $"QR {pending.RequestCode} của đơn này chưa được xử lý. Xác nhận đã nhận tiền hoặc hủy QR trước khi tạo QR mới.";
        PosSavedQr? saved = null;
        if (!string.IsNullOrWhiteSpace(pending.QrDataUrl))
            saved = await ReopenQrAsync(order.Id, pending.Id, ct);
        else
            message = $"QR {pending.RequestCode} đang chờ xác minh và chưa có ảnh mã. Mở lịch sử giao dịch để kiểm tra hoặc hủy lần tạo này trước khi tạo QR mới.";
        throw new PendingPaymentQrException(order.Id, pending.Id, saved, message);
    }

    public async Task<object> ConfirmManualQrAsync(int qrId, CancellationToken ct,
        BankNotificationReceipt? notification = null, BankNotificationWait? waiting = null)
    {
        // All manual, cashier and admin paths acquire evidence before the order.
        // SQL session locks are re-entrant when the bridge already owns this gate.
        await using var evidenceGate = await locks.AcquireAsync(db, 0, ct);
        var hint = await db.PosPaymentQrRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == qrId && x.StoreId == StoreId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy QR thanh toán.");
        await using var gate = await locks.AcquireAsync(db, hint.OrderId, ct);
        var qr = await db.PosPaymentQrRequests.Include(x => x.BankAccount).SingleAsync(x => x.Id == qrId && x.StoreId == StoreId, ct);
        // Admin command delivery may already have tracked this QR before waiting
        // for the order lock. Re-read its paid/cancelled state after acquiring it.
        await db.Entry(qr).ReloadAsync(ct);
        var order = await OrderAsync(qr.OrderId, ct);
        RequireTerminal(order);
        if (qr.ConfirmMode != BankQrConfirmMode.Manual || qr.QrRenderMode != BankQrRenderMode.LocalEmvQr)
            throw new InvalidOperationException("QR tự động phải được ACB xác nhận.");
        var adminApproval = qr.NotificationConfirmationSource is BankNotificationBridgeService.AdminApprovalSource or
            BankNotificationBridgeService.AdminReceiptApprovalSource or BankNotificationBridgeService.AdminCompletedSource or
            BankNotificationBridgeService.AdminReceiptCompletedSource;
        if (adminApproval && (waiting == null || waiting.StoreId != StoreId || waiting.QrId != qr.Id || waiting.OrderId != order.Id ||
            waiting.TerminalId != runtime.TerminalId || waiting.ShiftId != order.POSShiftId || waiting.BankAccountId != qr.BankAccountId || waiting.Amount != qr.Amount ||
            waiting.BankAccountFingerprint != BankNotificationParser.AccountFingerprint(qr.BankAccount) || !qr.BankAccount.IsActive ||
            order.POSShift.CurrentOrderId != null && order.POSShift.CurrentOrderId != order.Id ||
            qr.PaymentId == null && !AcbPaymentPolicy.MatchesFingerprint(order, waiting.CartFingerprint)))
            throw new InvalidOperationException("Đơn hoặc tài khoản nhận đã thay đổi sau xác nhận của admin. Cần đối soát lại.");
        if (notification != null)
        {
            var parsedSource=BankNotificationParser.Parse(notification.Content);
            if(parsedSource?.QrCode is string webReference&&System.Text.RegularExpressions.Regex.IsMatch(webReference,"^W[0-9]{12}$"))
                throw new InvalidOperationException("Thông báo này thuộc đơn website; không ghi thành khoản thu POS.");
            var confirmationSource = notification.ConfirmationSource;
            await db.Entry(notification).ReloadAsync(ct);
            if (await BankNotificationEvidence.ClaimedAsync(db, notification, waiting?.Id, ct) ||
                notification.AppliedWaitId != null && notification.AppliedWaitId != waiting?.Id)
                throw new InvalidOperationException("Khoản tiền đã được sử dụng cho một thanh toán khác.");
            if (waiting == null || notification.StoreId != StoreId || waiting.StoreId != StoreId ||
                waiting.QrId != qr.Id || waiting.OrderId != order.Id || waiting.TerminalId != runtime.TerminalId ||
                waiting.ShiftId != order.POSShiftId || waiting.BankAccountId != qr.BankAccountId ||
                notification.BankAccountId != qr.BankAccountId || notification.Amount != qr.Amount || waiting.Amount != qr.Amount)
                throw new InvalidOperationException("Thông báo không khớp khoản đang chờ của quầy.");
            if (notification.PaymentId == null && (!AcbPaymentPolicy.MatchesFingerprint(order, waiting.CartFingerprint) ||
                order.POSShift.Status != POSShiftStatus.Open || qr.PaymentId != null))
                throw new InvalidOperationException("Đơn đã thay đổi hoặc khoản tiền đã được xác nhận thủ công.");
            if (notification.PaymentId != null && (notification.AppliedWaitId != waiting.Id || notification.PaymentId != qr.PaymentId))
                throw new InvalidOperationException("Khoản tiền đã được sử dụng cho một thanh toán khác.");
            if (notification.PaymentId == null && !adminApproval) notification.ConfirmationSource = confirmationSource;
        }
        int? bankEvidence=notification is null?null:await NotificationProof(qr,notification,ct);
        if (qr.PaymentId.HasValue)
        {
            if (!order.Payments.Any(x => x.Id == qr.PaymentId && !x.IsDeleted && x.Amount == qr.Amount && x.ReferenceCode == qr.RequestCode))
                throw new InvalidOperationException("Khoản thanh toán của QR đã thay đổi. Hãy kiểm tra lịch sử thanh toán.");
            if(bankEvidence is int existingProof)
            {
                await using var linkTx=await db.Database.BeginTransactionAsync(ct);await LockNotificationProof(existingProof,ct);
                var priorPayment=order.Payments.Single(x=>x.Id==qr.PaymentId);
                if(priorPayment.BankVerifiedReceiptId is not null&&priorPayment.BankVerifiedReceiptId!=existingProof)throw new InvalidOperationException("Khoản thanh toán đã có bằng chứng khác.");
                priorPayment.BankVerifiedReceiptId=existingProof;await ClaimNotificationProof(existingProof,priorPayment.Id,ct);await db.SaveChangesAsync(ct);await linkTx.CommitAsync(ct);
            }
        }
        else
        {
            if (qr.Status != PosPaymentQrStatus.Pending || order.Status != OrderStatus.Draft)
                throw new InvalidOperationException("QR đã kết thúc, không thể ghi nhận thêm tiền.");
            if (qr.Amount <= 0 || qr.Amount != decimal.Truncate(qr.Amount) || qr.Amount >= 10000000000000000m)
                throw new InvalidOperationException("Số tiền QR không hợp lệ. Hãy đối chiếu tiền thực nhận trước khi xử lý.");
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var accountIdentity = GaoApp.Infrastructure.Services.Banking.GlobalBankReceiptLedger.AccountIdentity(qr.BankAccount.BankCode, qr.BankAccount.AccountNumber);
            await GaoApp.Infrastructure.Services.Banking.GlobalBankReceiptLedger.LockAsync(db, "GaoApp:BankAccountEvidence:" + accountIdentity, ct);
            if (db.Database.IsSqlServer())
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [StoreBankAccounts] WITH(HOLDLOCK) WHERE [StoreId]={StoreId} AND [Id]={qr.BankAccountId};", ct);
            await db.Entry(qr.BankAccount).ReloadAsync(ct);
            var accountVersion = db.Database.IsSqlServer()
                ? qr.BankAccount.RowVersion is { Length: 8 } version ? Convert.ToBase64String(version)
                    : throw new InvalidOperationException("Thiếu phiên bản tài khoản nhận nguyên thủy; cần đối soát trước khi xác nhận.")
                : Convert.ToBase64String(qr.BankAccount.RowVersion ?? []);
            if (GaoApp.Infrastructure.Services.Banking.GlobalBankReceiptLedger.AccountIdentity(qr.BankAccount.BankCode, qr.BankAccount.AccountNumber) != accountIdentity ||
                qr.AccountIdentitySnapshot is not null && (qr.AccountIdentitySnapshot != accountIdentity || qr.AccountVersionSnapshot != accountVersion))
                throw new InvalidOperationException("Tài khoản gốc của QR đã thay đổi. Dừng xác nhận và đối soát nguồn trước.");
            if (notification is null && await db.Set<BankVerifiedReceipt>().AnyAsync(x => x.AccountIdentity == accountIdentity &&
                x.RoutingProvenance == BankReceiptRoutingProvenance.UnmatchedOnly && x.AssetConfidence == BankReceiptAssetConfidence.CertifiedDistinct &&
                x.Amount == qr.Amount && x.OccurredAtUtc >= qr.CreatedAtUtc.AddSeconds(-5) && x.OccurredAtUtc <= qr.ExpireAtUtc.AddSeconds(5), ct))
                throw new InvalidOperationException("Có nguồn thông báo ngân hàng độc lập cùng tài khoản/số tiền/thời gian chưa chứng minh khác giao dịch. Cần đối soát trước, không ghi thêm khoản thu thủ công.");
            if(bankEvidence is int lockedProof)await LockNotificationProof(lockedProof,ct);
            var payment = new OrderPayment { StoreId = StoreId, OrderId = order.Id, Method = PaymentMethod.BankTransfer,
                Amount = qr.Amount, ReferenceCode = qr.RequestCode, Provider = qr.BankAccount.BankCode,BankVerifiedReceiptId=bankEvidence };
            db.OrderPayments.Add(payment);
            await db.SaveChangesAsync(ct);
            if(bankEvidence is int proofId)await ClaimNotificationProof(proofId,payment.Id,ct);
            qr.PaymentId = payment.Id;
            qr.Status = PosPaymentQrStatus.ManualConfirmed;
            qr.PaidAtUtc = qr.ManualConfirmedAtUtc = DateTime.UtcNow;
            if (!adminApproval) qr.ManualConfirmedByUserId = runtime.UserId;
            if (notification != null)
            {
                notification.PaymentId = payment.Id;
                notification.AppliedWaitId = waiting!.Id;
                notification.ConfirmedAtUtc = DateTime.UtcNow;
                if (!adminApproval) notification.ConfirmedByUserId = runtime.UserId;
                qr.NotificationReceiptId = notification.Id;
                if (adminApproval) notification.ConfirmationSource = BankNotificationBridgeService.AdminReceiptCompletedSource;
                else qr.NotificationConfirmationSource = notification.ConfirmationSource;
            }
            else if (qr.NotificationConfirmationSource == BankNotificationBridgeService.ManualReviewSource)
                qr.NotificationConfirmationSource = BankNotificationBridgeService.ManualCompletedSource;
            order.PaidTotal = order.Payments.Where(x => !x.IsDeleted).Sum(x => x.Amount);
            order.BalanceDue = Math.Max(0, order.GrandTotal - order.PaidTotal);
            order.ChangeDue = Math.Max(0, order.PaidTotal - order.GrandTotal);
            order.PaymentStatus = order.BalanceDue > 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Paid;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        if (notification != null)
            await notifier.NotifyTerminalAsync(StoreId, order.POSShift.TerminalId.ToString(), "qr_payment_recorded", order.Id,
                summaryChanged: true, paymentsChanged: true, message: "Đã xác nhận khoản chuyển khoản từ thông báo ACB.", ct: ct);
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
