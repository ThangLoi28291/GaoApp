using GaoApp.Application.Interfaces.Services.POSPaymentQrs;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services;

public sealed record CustomerPaymentDisplayCommand(int OrderId, int? QrId, decimal Amount, int Method);
public sealed record CustomerPaymentDisplayReceipt(string Key, object Payload, DateTime ValidUntilUtc)
{
    // SQL datetime2 retains UTC clock values but drops DateTime.Kind. Always serialize a Z suffix.
    public DateTime ValidUntilUtc { get; init; } = DateTime.SpecifyKind(ValidUntilUtc, DateTimeKind.Utc);
}
public sealed record CustomerPaymentDisplaySnapshot(int StoreId, int TerminalId, string Mode,
    int? OrderId, string Key, object? Payload, DateTime ValidUntilUtc, CustomerPaymentDisplayReceipt? Receipt = null)
{
    public DateTime ValidUntilUtc { get; init; } = DateTime.SpecifyKind(ValidUntilUtc, DateTimeKind.Utc);
}

public sealed class CustomerPaymentDisplayService(AppDbContext db, CustomerPaymentDisplayState state,
    CustomerDepositDisplayState deposits, ILocalVietQrGenerator generator)
{
    private static readonly TimeSpan ReceiptDuration = TimeSpan.FromSeconds(15);

    // Hub scopes do not have HTTP tenant middleware. Every query is explicitly store/terminal bounded.
    private Task<POSShift?> ShiftAsync(int store, int terminal, CancellationToken ct) => db.POSShifts.AsNoTracking()
        .Where(s => s.StoreId == store && s.TerminalId == terminal && s.Status == POSShiftStatus.Open)
        .OrderByDescending(s => s.Id).FirstOrDefaultAsync(ct);

    public async Task ApplyAsync(int store, int terminal, string eventType, CustomerPaymentDisplayCommand command, CancellationToken ct)
    {
        var shift = await ShiftAsync(store, terminal, ct);
        if (shift == null || command.OrderId <= 0 || shift.CurrentOrderId != command.OrderId ||
            !await db.Orders.AsNoTracking().AnyAsync(o => o.StoreId == store && o.Id == command.OrderId &&
                o.POSShiftId == shift.Id && o.Status == OrderStatus.Draft, ct))
            throw new ValidationAppException("Thông tin hiển thị phải thuộc giỏ hiện tại của đúng ca và quầy.");
        if (eventType == "customer_payment_qr_created")
        {
            if (command.QrId is not > 0 || await QrAsync(store, shift, command.OrderId, command.QrId, ct) == null)
                throw new ValidationAppException("QR không còn hiệu lực hoặc không thuộc giỏ hiện tại.");
            state.Set(store, terminal, shift.Id, command.OrderId, "qr", command.QrId, 0, 0);
        }
        else if (eventType == "customer_payment_preview")
        {
            if (command.Amount < 0 || command.Amount >= 10000000000000000m || command.Amount != decimal.Truncate(command.Amount) ||
                !Enum.IsDefined((PaymentMethod)command.Method))
                throw new ValidationAppException("Thông tin xem trước thanh toán không hợp lệ.");
            state.Set(store, terminal, shift.Id, command.OrderId, "preview", null, command.Amount, command.Method);
        }
        else if (eventType == "customer_payment_hide")
            state.Set(store, terminal, shift.Id, command.OrderId, "hide", null, 0, 0);
        else throw new ValidationAppException("Thao tác hiển thị không hợp lệ.");
        deposits.Clear(store, terminal);
    }

    public async Task ShowDepositQrAsync(int store, int terminal,
        GaoApp.Application.Interfaces.Services.Orders.DepositQrPreviewDto qr, CancellationToken ct)
    {
        var shift = await ShiftAsync(store, terminal, ct);
        if (shift != null) deposits.Set(store, terminal, qr, shift.Id, shift.CurrentOrderId);
    }

    public async Task ShowDepositReceiptAsync(int store, int terminal, int depositId, CancellationToken ct)
    {
        var shift = await ShiftAsync(store, terminal, ct);
        if (shift == null) return;
        // Re-read the committed receipt; never echo request.Amount or an unbound replay.
        var entry = await db.Set<CustomerDepositEntry>().AsNoTracking().SingleOrDefaultAsync(e => e.StoreId == store &&
            e.POSShiftId == shift.Id && e.CustomerDepositId == depositId && e.Kind == "Receive", ct);
        deposits.Clear(store, terminal);
        if (entry != null && entry.CreatedAtUtc + ReceiptDuration > DateTime.UtcNow)
            deposits.SetReceipt(store, terminal, shift.Id, shift.CurrentOrderId, entry.Id, entry.Amount, entry.CreatedAtUtc + ReceiptDuration);
    }

    public async Task<CustomerPaymentDisplaySnapshot> GetAsync(int store, int terminal, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        CustomerPaymentDisplaySnapshot Idle(int? order = null) => new(store, terminal, "idle", order, "idle", null, now.AddSeconds(5));
        if (store <= 0 || terminal <= 0) return Idle();
        var shift = await ShiftAsync(store, terminal, ct);
        if (shift == null) return Idle();
        var deposit = deposits.GetState(store, terminal);
        if (deposit?.ShiftId == shift.Id && deposit.OrderId == shift.CurrentOrderId && IsSafeQrImage(deposit.Qr.QrDataUrl))
            return new(store, terminal, "qr", null, $"deposit-qr:{deposit.Qr.Content}", new
            {
                deposit.Qr.QrDataUrl, deposit.Qr.Amount, deposit.Qr.Content, deposit.Qr.BankName,
                deposit.Qr.AccountNumber, deposit.Qr.AccountName, kind = "deposit", title = "Quét mã để đặt cọc",
                intro = "Chuyển tiền đặt cọc đến"
            }, deposit.ExpiresAtUtc);
        var depositReceipt = deposits.GetReceipt(store, terminal);
        if (depositReceipt?.ShiftId == shift.Id && depositReceipt.OrderId == shift.CurrentOrderId)
            return new(store, terminal, "recorded", null, $"deposit-receipt:{depositReceipt.EntryId}", new
            {
                finalized = true, kind = "deposit", title = "Đã nhận tiền đặt cọc",
                message = "Phiếu cọc đã được nhân viên ghi nhận. Cảm ơn quý khách!", paidAmount = depositReceipt.Amount, durationMs = 5000
            }, depositReceipt.ExpiresAtUtc);

        var intent = state.Get(store, terminal);
        if (intent?.ShiftId != shift.Id) intent = null;
        var orderId = shift.CurrentOrderId ?? intent?.OrderId;
        if (orderId == null) return Idle();
        var order = await db.Orders.AsNoTracking().Include(o => o.Payments)
            .SingleOrDefaultAsync(o => o.StoreId == store && o.Id == orderId && o.POSShiftId == shift.Id, ct);
        if (order == null || order.Status is not (OrderStatus.Draft or OrderStatus.Completed)) return Idle();
        if (shift.CurrentOrderId != order.Id && (order.Status != OrderStatus.Completed ||
            order.CompletedAtUtc == null || order.CompletedAtUtc + ReceiptDuration <= now)) return Idle();
        if (intent?.OrderId != order.Id) intent = null;
        var payments = order.Payments.Where(p => p.StoreId == store && !p.IsDeleted).ToList();
        var paid = payments.Sum(p => p.Amount) + order.DepositAmount;
        var balance = Math.Max(0, order.GrandTotal - paid);
        var last = payments.OrderByDescending(p => p.Id).FirstOrDefault();

        // The receipt is independent of preview/hide commands, including modal-close races after collection.
        CustomerPaymentDisplayReceipt? receipt = null;
        if (last != null && last.CreatedAtUtc + ReceiptDuration > now)
        {
            var source = await SourceAsync(store, order.Id, last, ct);
            var finalized = order.Status == OrderStatus.Completed && balance == 0;
            receipt = new($"payment:{order.Id}:{last.Id}:{paid}:{finalized}", new
            {
                finalized, paidAmount = paid, remainingAmount = balance, changeAmount = Math.Max(0, paid - order.GrandTotal),
                confirmationSource = source, durationMs = 4500
            }, last.CreatedAtUtc + ReceiptDuration);
        }
        if (intent?.Mode == "qr" && last != null && last.CreatedAtUtc < intent.AtUtc) receipt = null;

        if (intent?.Mode == "qr" && order.Status == OrderStatus.Draft)
        {
            var qr = await QrAsync(store, shift, order.Id, intent.QrId, ct);
            if (qr != null)
                return new(store, terminal, "qr", order.Id, $"qr:{qr.Value.Id}:{intent.Version}", qr.Value.Payload,
                    qr.Value.ExpiresAtUtc < intent.ExpiresAtUtc ? qr.Value.ExpiresAtUtc : intent.ExpiresAtUtc,
                    last != null && last.CreatedAtUtc >= intent.AtUtc ? receipt : null);
        }
        if (intent?.Mode == "preview" && order.Status == OrderStatus.Draft)
            return new(store, terminal, "preview", order.Id, $"preview:{intent.Version}:{Convert.ToHexString(order.RowVersion)}", new
            {
                method = intent.Method, amount = intent.Amount, balance, grandTotal = order.GrandTotal, paidTotal = paid,
                expectedBalance = Math.Max(0, balance - intent.Amount), expectedChange = Math.Max(0, intent.Amount - balance)
            }, intent.ExpiresAtUtc, receipt);
        return Idle(order.Id) with { Receipt = receipt };
    }

    private async Task<(int Id, object Payload, DateTime ExpiresAtUtc)?> QrAsync(int store, POSShift shift, int order, int? id, CancellationToken ct)
    {
        var qr = await db.PosPaymentQrRequests.AsNoTracking().Include(q => q.BankAccount)
            .SingleOrDefaultAsync(q => q.StoreId == store && q.OrderId == order && q.Id == id && q.PaymentId == null &&
                q.Status == PosPaymentQrStatus.Pending && q.ExpireAtUtc > DateTime.UtcNow, ct);
        if (qr == null || qr.BankAccount.StoreId != store || !qr.BankAccount.IsActive || !IsSafeQrImage(qr.QrDataUrl)) return null;
        var session = await db.Set<AcbQrSession>().AsNoTracking().SingleOrDefaultAsync(s => s.StoreId == store && s.QrRequestId == qr.Id, ct);
        if (session != null && (session.OrderId != order || session.TerminalId != shift.TerminalId || session.ShiftId != shift.Id ||
            session.Status != AcbSessionStatus.Pending || session.PaymentId != null)) return null;
        if (session == null)
        {
            if (qr.ConfirmMode != BankQrConfirmMode.Manual) return null;
            // The stored image must still describe the configured recipient; do not replace a saved QR with a new one.
            try
            {
                if (qr.QrRawText != generator.BuildPayload(qr.BankAccount.VietQrBankBin ?? "", qr.BankAccount.AccountNumber, qr.Amount, qr.Content)) return null;
            }
            catch (InvalidOperationException) { return null; }
        }
        return (qr.Id, new
        {
            qrId = qr.Id, orderId = order, qr.QrDataUrl, qr.Amount, qr.Content, qr.BankAccount.BankName,
            accountNumber = session?.VirtualAccount ?? qr.BankAccount.AccountNumber, qr.BankAccount.AccountName
        }, qr.ExpireAtUtc);
    }

    private async Task<string> SourceAsync(int store, int order, OrderPayment payment, CancellationToken ct)
    {
        if (payment.Method == PaymentMethod.Cash) return "Nhân viên đã ghi nhận tiền mặt.";
        if (payment.MetadataJson?.Contains("\"offline-manual\"", StringComparison.Ordinal) == true)
            return "Khoản thu thủ công khi mất kết nối đã được đồng bộ.";
        if (payment.Method != PaymentMethod.BankTransfer) return "Nhân viên đã ghi nhận thanh toán.";
        var qr = await db.PosPaymentQrRequests.AsNoTracking().SingleOrDefaultAsync(q => q.StoreId == store && q.OrderId == order && q.PaymentId == payment.Id, ct);
        var session = await db.Set<AcbQrSession>().AsNoTracking().SingleOrDefaultAsync(s => s.StoreId == store && s.OrderId == order && s.PaymentId == payment.Id, ct);
        if (session != null) return AcbConfirmationAudit.ForSession(session, new Dictionary<int, string>(), true).Label;
        return AcbConfirmationAudit.ForManual(qr, payment, new Dictionary<int, string>()).Label;
    }

    public static bool IsSafeQrImage(string? value) => value is { Length: > 24 and < 3000000 } &&
        (value.StartsWith("data:image/png;base64,", StringComparison.Ordinal) ||
         value.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) ||
         value.StartsWith("data:image/webp;base64,", StringComparison.Ordinal));
}
