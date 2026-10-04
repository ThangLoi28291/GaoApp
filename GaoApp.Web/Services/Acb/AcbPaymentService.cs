using System.Globalization;
using System.Text.Json;
using GaoApp.Application.DTOs.POSPaymentQrs;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Common.POS;
using Microsoft.EntityFrameworkCore;
using static GaoApp.Web.Services.Acb.AcbProtocol;

namespace GaoApp.Web.Services.Acb;

public sealed partial class AcbPaymentService(AppDbContext db, AcbProtocol protocol, IPOSService pos,
    IPOSRuntimeContextAccessor runtime, IPosRealtimeNotifier notifier, IAcbOrderLockProvider locks)
{
    private int StoreId => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new InvalidOperationException("Chưa xác định cửa hàng.");
    private IQueryable<AcbQrSession> Sessions => db.Set<AcbQrSession>().Where(x => x.StoreId == StoreId);
    public Task<StoreAcbSettings?> SettingsAsync(CancellationToken ct) => db.Set<StoreAcbSettings>().SingleOrDefaultAsync(x => x.StoreId == StoreId, ct);
    private async Task<Order> OrderAsync(int id, CancellationToken ct) => await db.Orders
        .Include(x => x.POSShift).Include(x => x.Lines).ThenInclude(x => x.Variant).Include(x => x.Payments)
        .SingleOrDefaultAsync(x => x.Id == id && x.StoreId == StoreId, ct) ?? throw new InvalidOperationException("Không tìm thấy đơn hàng.");
    private void RequireTerminal(Order order, AcbQrSession? session = null)
    {
        if (runtime.StoreId != StoreId || runtime.TerminalId is not > 0 ||
            order.POSShift.TerminalId != runtime.TerminalId || order.POSShift.Status != POSShiftStatus.Open ||
            (session != null && (session.ShiftId != order.POSShiftId || session.TerminalId != runtime.TerminalId)))
            throw new InvalidOperationException("Giao dịch phải được xử lý tại đúng ca và máy tính tiền đã tạo QR.");
    }

    public Task<POSPaymentQrDto?> TryCreateAsync(int orderId, CancellationToken ct) => TryCreateAsync(orderId, null, ct);

    public async Task<POSPaymentQrDto?> TryCreateAsync(int orderId, CreatePOSPaymentQrRequest? request, CancellationToken ct)
    {
        var settings = await SettingsAsync(ct);
        if (settings?.Enabled != true) return null;
        _ = ValidateEnvironment(settings.TokenEndpoint, settings.ApiBaseUrl, settings.QrEndpoint);
        await using var gate = await locks.AcquireAsync(db, orderId, ct);
        var order = await OrderAsync(orderId, ct);
        RequireTerminal(order);
        var existing = request?.ClientRequestId is Guid key
            ? await Sessions.SingleOrDefaultAsync(x => x.OrderId == orderId && db.PosPaymentQrRequests.Any(q => q.Id == x.QrRequestId && q.ClientRequestId == key), ct)
            : await Sessions.OrderByDescending(x => x.Id).FirstOrDefaultAsync(x => x.OrderId == orderId && x.Status != AcbSessionStatus.Cancelled && x.PaymentId == null, ct);
        if (existing != null)
        {
            if (request?.ClientRequestId != null)
            {
                if (request.Amount is > 0 && request.Amount != existing.Amount)
                    throw new InvalidOperationException("Lần tạo QR này đã có số tiền khác. Hãy mở lại QR hoặc chọn tạo QR mới.");
                if (existing.Status == AcbSessionStatus.Creating)
                    throw new InvalidOperationException("Lần tạo QR đang chờ ACB xác minh. Hãy tra cứu lịch sử QR trước khi tạo lần mới.");
                return (await ReopenQrAsync(orderId, existing.QrRequestId, ct)).Qr;
            }
            if (existing.Status == AcbSessionStatus.ReviewRequired && existing.ReviewReason == AcbPaymentPolicy.CartChangedReason &&
                AcbPaymentPolicy.MatchesFingerprint(order, existing.CartFingerprint))
                await RetrieveLockedAsync(existing, ct, AcbConfirmationSource.QrRecoveryCheck); // Recover the original QR, never issue another one.
            if (existing.Status == AcbSessionStatus.ReviewRequired) throw new InvalidOperationException(existing.ReviewReason ?? "Giao dịch cần kiểm tra.");
            if (existing.Status == AcbSessionStatus.Creating) throw new GaoApp.Application.Common.Exceptions.ConflictAppException($"QR {existing.ProviderOrderId} của đơn {orderId} đang chờ xác minh sau lần tạo trước. Mở Tra cứu chuyển khoản ACB để kiểm tra và hủy lần tạo cũ trước khi tạo lại.");
            if (!AcbPaymentPolicy.MatchesFingerprint(order, existing.CartFingerprint) || AcbPaymentPolicy.Balance(order) != existing.Amount)
                throw new InvalidOperationException("Đơn đã thay đổi. Hãy hủy QR cũ trước khi tạo QR mới.");
            return await MapQrAsync(existing, ct);
        }
        await RequirePreviousQrResolvedAsync(order, ct);
        if (!AcbPaymentPolicy.Eligible(order)) return null;
        if (await Sessions.AnyAsync(x => x.OrderId == orderId &&
            (x.Status == AcbSessionStatus.Creating || x.Status == AcbSessionStatus.ReviewRequired), ct))
            throw new InvalidOperationException("Đơn còn QR đang xác minh hoặc cần kiểm tra. Hãy xử lý QR đó trong lịch sử trước khi tạo thêm.");
        if (order.Status != OrderStatus.Draft) throw new InvalidOperationException("Đơn không còn là giỏ đang thanh toán.");
        var amount = request?.Amount is > 0 ? request.Amount.Value : AcbPaymentPolicy.Balance(order);
        if (amount <= 0 || amount != decimal.Truncate(amount) || amount > int.MaxValue)
            throw new InvalidOperationException("Số tiền QR phải là số đồng nguyên dương, không vượt quá giới hạn ACB.");
        var bank = await db.StoreBankAccounts.SingleOrDefaultAsync(x => x.Id == settings.BankAccountId && x.StoreId == StoreId && x.IsActive, ct)
            ?? throw new InvalidOperationException("Chưa cấu hình tài khoản ACB đang hoạt động cho cửa hàng.");
        if (settings.MerchantId.Length > 30 || settings.BeneficiaryName.Length > 30)
            throw new InvalidOperationException("MerchantId và tên người thụ hưởng ACB tối đa 30 ký tự.");
        var providerOrderId = $"P{Guid.NewGuid():N}"; // Local placeholder until the database assigns the QR id.
        var qr = new PosPaymentQrRequest
        {
            StoreId = StoreId, OrderId = orderId, BankAccountId = bank.Id, Amount = amount,
            ClientRequestId = request?.ClientRequestId,
            RequestCode = providerOrderId, Content = $"Thanh toan don {orderId}",
            QrRenderMode = BankQrRenderMode.ProviderApi, ConfirmMode = BankQrConfirmMode.Callback,
            Status = PosPaymentQrStatus.Pending, ExpireAtUtc = DateTime.UtcNow.AddMinutes(10)
        };
        db.PosPaymentQrRequests.Add(qr);
        await db.SaveChangesAsync(ct);
        // QR ids are global within this multi-store database. ACB orderId is limited to 13 characters.
        providerOrderId = $"GA{qr.Id:D10}";
        qr.RequestCode = providerOrderId;
        var session = new AcbQrSession
        {
            StoreId = StoreId, QrRequestId = qr.Id, OrderId = orderId, ShiftId = order.POSShiftId,
            TerminalId = order.POSShift.TerminalId, CashierId = runtime.UserId ?? 0,
            Amount = amount, CartFingerprint = AcbPaymentPolicy.Fingerprint(order),
            ProviderOrderId = providerOrderId, TraceNumber = Guid.NewGuid().ToString(), Status = AcbSessionStatus.Creating
        };
        db.Set<AcbQrSession>().Add(session);
        await db.SaveChangesAsync(ct); // Persist the bank correlation BEFORE the network request.
        JsonElement result;
        try
        {
            result = await protocol.CallAsync(settings, HttpMethod.Post, settings.QrEndpoint, new
            {
                traceNumber = session.TraceNumber, merchantId = settings.MerchantId,
                terminalId = session.TerminalId.ToString(CultureInfo.InvariantCulture),
                userId = session.CashierId.ToString(CultureInfo.InvariantCulture), orderId = providerOrderId,
                virtualAccountPrefix = settings.VirtualAccountPrefix, beneficiaryName = settings.BeneficiaryName,
                amount = (int)amount, description = qr.Content
            }, ct);
        }
        catch (AcbApiException error) when (error.BusinessRequestNotSent)
        {
            // Token acquisition failed before any initiate request was sent. Release only this local attempt.
            // Once initiate has been sent, preserve Creating for reconciliation instead of assuming failure.
            session.Status = AcbSessionStatus.Cancelled;
            session.ReviewReason = "Lần tạo QR kết thúc do không lấy được token; chưa gửi yêu cầu tạo QR tới ACB.";
            qr.Status = PosPaymentQrStatus.Cancelled;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        var body = Path(result, "responseBody");
        qr.QrDataUrl = Text(body, "qrDataUrl");
        if (!qr.QrDataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ACB chưa trả ảnh QR hợp lệ. Hãy tra cứu lại giao dịch.");
        session.VirtualAccount = Text(body, "virtualAccount");
        var trace = Text(body, "traceNumber");
        if (!string.IsNullOrWhiteSpace(trace)) session.TraceNumber = trace;
        session.Status = AcbSessionStatus.Pending;
        await db.SaveChangesAsync(ct);
        return await MapQrAsync(session, ct);
    }

    private async Task<POSPaymentQrDto> MapQrAsync(AcbQrSession session, CancellationToken ct)
    {
        var qr = await db.PosPaymentQrRequests.Include(x => x.BankAccount).SingleAsync(x => x.Id == session.QrRequestId && x.StoreId == StoreId, ct);
        return new POSPaymentQrDto
        {
            Id = qr.Id, OrderId = qr.OrderId, BankAccountId = qr.BankAccountId,
            BankCode = qr.BankAccount.BankCode, BankName = qr.BankAccount.BankName,
            AccountName = qr.BankAccount.AccountName, AccountNumber = session.VirtualAccount,
            Amount = qr.Amount, Content = qr.Content, RequestCode = qr.RequestCode,
            Status = qr.Status, QrDataUrl = qr.QrDataUrl ?? "", ExpireAtUtc = qr.ExpireAtUtc,
            AutomaticConfirmation = true
        };
    }

    public async Task<bool> CancelAsync(int qrId, CancellationToken ct)
    {
        var hint = await Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.QrRequestId == qrId, ct);
        if (hint == null) return false;
        await using var gate = await locks.AcquireAsync(db, hint.OrderId, ct);
        var session = await Sessions.SingleAsync(x => x.Id == hint.Id, ct);
        // A caller may have tracked this session before acquiring the lock. Preserve any callback that won the race.
        await db.Entry(session).ReloadAsync(ct);
        RequireTerminal(await OrderAsync(session.OrderId, ct), session);
        if (session.Status == AcbSessionStatus.Cancelled) return true;
        var confirmedAbsent = await RetrieveLockedAsync(session, ct, AcbConfirmationSource.CancellationCheck); // Detect a payment racing with cancellation.
        if (session.Status is AcbSessionStatus.Received or AcbSessionStatus.Completed or AcbSessionStatus.ReviewRequired)
            throw new InvalidOperationException("QR đã có giao dịch nhận tiền hoặc cần kiểm tra, không thể hủy thanh toán đang chờ.");
        var settings = await SettingsAsync(ct) ?? throw new InvalidOperationException("Thiếu cấu hình ACB.");
        var qr = await db.PosPaymentQrRequests.SingleAsync(x => x.Id == qrId && x.StoreId == StoreId, ct);
        try
        {
            var result = await protocol.CallAsync(settings, HttpMethod.Delete,
                settings.ApiBaseUrl.TrimEnd('/') + "/acb/open/payments/qr-payment/v1/cancellation",
                new { traceNumber = session.TraceNumber, orderId = session.ProviderOrderId, amount = session.Amount }, ct);
            if (Text(Path(result, "responseBody"), "status") != "SUCCESS") throw new InvalidOperationException("ACB chưa xác nhận hủy QR.");
        }
        catch (AcbApiException error) when (error.ProviderResponseCode == "30020402")
        {
            // ACB documents 30020402 as Data not found. Release only an unconfirmed create,
            // after a fresh, complete retrieval found no matching order and no transaction evidence.
            if (!confirmedAbsent || session.Status != AcbSessionStatus.Creating ||
                !string.IsNullOrEmpty(session.VirtualAccount) || !string.IsNullOrEmpty(qr.QrDataUrl) ||
                await db.Set<AcbPaymentTransaction>().AnyAsync(x => x.StoreId == StoreId && x.SessionId == session.Id, ct)) throw;
            session.ReviewReason = "Đã hủy lần tạo QR chưa hoàn tất: tra cứu không có đơn tại ACB, API hủy trả mã 30020402 (không tìm thấy dữ liệu).";
        }
        session.Status = AcbSessionStatus.Cancelled;
        qr.Status = PosPaymentQrStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // A durable server queue invokes this immediately; the POS timer is not involved.
    // Retrieve supplies the bank transaction id because the notification contract does not
    // document its mapping to transactionDetail.transactionNumber or the checksum algorithm.
    public async Task<AcbCallbackProcessingResult> CallbackAsync(JsonElement payload, CancellationToken ct, int? receiptId = null)
    {
        var callbackSource = Text(Path(payload, "requestParameters", "request", "requestMeta"), "requestCode") == "TRANSACTION_HISTORY"
            ? AcbConfirmationSource.DailyCallback : AcbConfirmationSource.Callback;
        var transactions = Items(Path(payload, "requestParameters", "request", "requestParams", "transactions")).ToList();
        var ids = transactions.Select(t => Text(Path(t, "transactionEntityAttribute"), "custom4"))
            .Where(x => x.Length > 0).Distinct().ToList();
        var settings = await SettingsAsync(ct) ?? throw new InvalidOperationException("Thiếu cấu hình ACB.");
        var accountNumber = await db.StoreBankAccounts.Where(x => x.StoreId == StoreId && x.Id == settings.BankAccountId)
            .Select(x => x.AccountNumber).SingleOrDefaultAsync(ct);
        var awaitingBank = false;
        var unmatched = transactions.Any(t => string.IsNullOrWhiteSpace(Text(Path(t, "transactionEntityAttribute"), "custom4")));
        var needsReview = false;
        Exception? bankFailure = null;
        foreach (var id in ids)
        {
            var hint = await Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderOrderId == id, ct);
            if (hint == null) { unmatched = true; continue; }
            await using var gate = await locks.AcquireAsync(db, hint.OrderId, ct);
            var session = await Sessions.SingleAsync(x => x.Id == hint.Id, ct);
            var qr = await db.PosPaymentQrRequests.SingleAsync(x => x.Id == session.QrRequestId && x.StoreId == StoreId, ct);
            qr.CallbackRawJson = payload.GetRawText();
            var related = transactions.Where(t => Text(Path(t, "transactionEntityAttribute"), "custom4") == id).ToList();
            var correction = related.Any(t => Text(t, "transactionStatus") == "ERRORCORRECTED" || Text(t, "debitOrCredit") == "debit");
            var mismatch = related.Any(t =>
            {
                var attributes = Path(t, "transactionEntityAttribute");
                var merchant = Text(attributes, "custom1");
                var terminal = Text(attributes, "custom2");
                var account = Text(attributes, "beneficiaryAccountNumber");
                var virtualAccount = Text(attributes, "virtualAccount");
                return (merchant.Length > 0 && merchant != settings.MerchantId) ||
                    (terminal.Length > 0 && terminal != session.TerminalId.ToString(CultureInfo.InvariantCulture)) ||
                    (virtualAccount.Length > 0 && virtualAccount != session.VirtualAccount) ||
                    (account.Length > 0 && account != accountNumber && account != session.VirtualAccount);
            });
            var moneyMismatch = related.Count(t => Text(t, "transactionStatus") == "COMPLETED") > 1 ||
                related.Any(t => Text(t, "transactionStatus") == "COMPLETED" && Money(t, "amount") != session.Amount);
            if (correction || mismatch || moneyMismatch)
            {
                session.Status = AcbSessionStatus.ReviewRequired;
                needsReview = true;
                session.ReviewReason = correction ? "ACB thông báo báo nợ hoặc điều chỉnh/hủy giao dịch (ERRORCORRECTED); cần kiểm tra khoản đã nhận."
                    : moneyMismatch ? "Callback báo số tiền hoặc số lần chuyển không khớp QR; cần kiểm tra."
                    : "Thông tin cửa hàng, máy hoặc tài khoản trong callback không khớp QR; cần kiểm tra.";
                qr.Status = PosPaymentQrStatus.Failed;
                await db.SaveChangesAsync(ct);
                await NotifyAsync(session, ct);
                continue; // Never automatically undo an order, stock movement or cash payment.
            }
            await db.SaveChangesAsync(ct);
            try { await RetrieveLockedAsync(session, ct, callbackSource, receiptId); }
            catch (Exception error) when (error is AcbApiException or HttpRequestException or JsonException ||
                (error is OperationCanceledException && !ct.IsCancellationRequested))
            {
                // One failing bank lookup must not starve the other orders in a 1,000-row page.
                bankFailure ??= error;
                continue;
            }
            awaitingBank |= session.Status is AcbSessionStatus.Pending or AcbSessionStatus.Creating or AcbSessionStatus.Cancelled;
            needsReview |= session.Status == AcbSessionStatus.ReviewRequired;
            await NotifyAsync(session, ct);
        }
        if (bankFailure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(bankFailure).Throw();
        return new(awaitingBank, unmatched, needsReview);
    }

    private async Task<bool> RetrieveLockedAsync(AcbQrSession session, CancellationToken ct,
        AcbConfirmationSource source, int? callbackReceiptId = null)
    {
        var settings = await SettingsAsync(ct) ?? throw new InvalidOperationException("Thiếu cấu hình ACB.");
        var created = new DateTimeOffset(DateTime.SpecifyKind(session.CreatedAtUtc, DateTimeKind.Utc)).ToOffset(TimeSpan.FromHours(7)).Date;
        var page = 0;
        var confirmedAbsent = true;
        var bankEvidenceMismatch = false;
        var freshPaidEvidence = false;
        var recoveringCartReview = session.Status == AcbSessionStatus.ReviewRequired &&
            session.ReviewReason == AcbPaymentPolicy.CartChangedReason;
        do
        {
            var url = settings.ApiBaseUrl.TrimEnd('/') + "/acb/open/payments/qr-payment/v1/retrieve" +
                $"?fromDate={created.AddDays(-1):yyyy-MM-dd}&toDate={DateTime.UtcNow.AddHours(7).Date.AddDays(1):yyyy-MM-dd}" +
                $"&orderId={Uri.EscapeDataString(session.ProviderOrderId)}&traceNumber={Uri.EscapeDataString(session.TraceNumber)}&page={page}&size=50";
            var response = await protocol.CallAsync(settings, HttpMethod.Get, url, null, ct);
            session.LastRetrieveJson = response.GetRawText();
            session.LastRetrievedAtUtc = DateTime.UtcNow;
            var body = Path(response, "responseBody");
            var bankOrders = Path(body, "orders");
            var totalPagesValue = Path(body, "pagination", "totalPages");
            if (bankOrders.ValueKind != JsonValueKind.Array || totalPagesValue.ValueKind != JsonValueKind.Number ||
                !totalPagesValue.TryGetInt32(out var totalPages) || totalPages < 0) confirmedAbsent = false;
            foreach (var bankOrder in Items(bankOrders))
            {
                if (Text(bankOrder, "orderId") != session.ProviderOrderId) continue;
                confirmedAbsent = false;
                if (Text(bankOrder, "traceNumber") != session.TraceNumber || Money(bankOrder, "amount") != session.Amount)
                { bankEvidenceMismatch = true; session.Status = AcbSessionStatus.ReviewRequired; session.ReviewReason = "Thông tin QR từ ACB không khớp yêu cầu đã lưu."; }
                foreach (var tx in Items(Path(bankOrder, "transactionDetail")))
                {
                    var number = Text(tx, "transactionNumber");
                    if (string.IsNullOrWhiteSpace(number))
                    { bankEvidenceMismatch = true; session.Status = AcbSessionStatus.ReviewRequired; session.ReviewReason = "ACB trả giao dịch thiếu mã đối soát."; continue; }
                    var saved = await db.Set<AcbPaymentTransaction>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.SessionId == session.Id && x.TransactionNumber == number, ct);
                    if (saved == null)
                    {
                        saved = new AcbPaymentTransaction { StoreId = StoreId, SessionId = session.Id, TransactionNumber = number };
                        db.Add(saved);
                    }
                    saved.Amount = Money(tx, "transactionAmount"); saved.Status = Text(tx, "transactionStatus");
                    freshPaidEvidence |= IsPaid(saved.Status);
                    saved.Content = Text(tx, "transactionContent"); saved.PostedAt = Text(tx, "origPostDate");
                    await db.SaveChangesAsync(ct);
                }
                if (Text(bankOrder, "status") == "CANCELLED" && session.Status is AcbSessionStatus.Creating or AcbSessionStatus.Pending)
                    session.Status = AcbSessionStatus.Cancelled;
            }
            page++;
            if (page >= Money(Path(body, "pagination"), "totalPages")) break;
            if (page >= 100) throw new InvalidOperationException("Kết quả ACB quá nhiều trang, cần kiểm tra giao dịch.");
        } while (true);
        var transactions = await db.Set<AcbPaymentTransaction>().Where(x => x.StoreId == StoreId && x.SessionId == session.Id).ToListAsync(ct);
        var reason = AcbPaymentPolicy.ReviewReason(session, await OrderAsync(session.OrderId, ct), transactions);
        if (!bankEvidenceMismatch)
        {
            // Keep unrelated review holds (cancellation, bank corrections, mismatched routing).
            // Only the legacy cart check can recover after a fresh, matching bank lookup.
            if (reason != null && (session.Status != AcbSessionStatus.ReviewRequired || recoveringCartReview))
            { session.Status = AcbSessionStatus.ReviewRequired; session.ReviewReason = reason; }
            else if (reason == null && !confirmedAbsent && (!recoveringCartReview || freshPaidEvidence) &&
                (session.Status is AcbSessionStatus.Pending or AcbSessionStatus.Creating || recoveringCartReview) &&
                transactions.Any(x => IsPaid(x.Status)))
            {
                session.Status = AcbSessionStatus.Received;
                session.ReviewReason = null;
                // The order lock serializes all verification paths. Save provenance together
                // with the first valid Received transition, never from the later /complete call.
                // Existing posted payments without audit metadata remain unknown.
                if (session.ConfirmationSource == null && session.PaymentId == null)
                {
                    session.ConfirmationSource = source;
                    session.ConfirmedAtUtc = DateTime.UtcNow;
                    var callback = source is AcbConfirmationSource.Callback or AcbConfirmationSource.DailyCallback;
                    session.ConfirmedByUserId = callback || source == AcbConfirmationSource.ScheduledCheck ? null : runtime.UserId;
                    session.ConfirmationCallbackReceiptId = callback ? callbackReceiptId : null;
                }
            }
        }
        var qr = await db.PosPaymentQrRequests.SingleAsync(x => x.Id == session.QrRequestId && x.StoreId == StoreId, ct);
        qr.Status = session.Status switch
        {
            AcbSessionStatus.Completed or AcbSessionStatus.Received => PosPaymentQrStatus.Paid,
            AcbSessionStatus.Cancelled => PosPaymentQrStatus.Cancelled,
            AcbSessionStatus.ReviewRequired => PosPaymentQrStatus.Failed,
            _ => PosPaymentQrStatus.Pending
        };
        if (session.Status is AcbSessionStatus.Received or AcbSessionStatus.Completed)
            qr.PaidAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return confirmedAbsent;
    }

    private Task NotifyAsync(AcbQrSession session, CancellationToken ct) => notifier.NotifyTerminalAsync(StoreId,
        session.TerminalId.ToString(CultureInfo.InvariantCulture), "acb_payment_changed", session.OrderId,
        paymentsChanged: true, message: session.ReviewReason ?? "ACB đã cập nhật trạng thái chuyển khoản.", ct: ct);

    public async Task<object> StatusAsync(int qrId, bool refresh, CancellationToken ct, bool manual = false)
    {
        var hint = await Sessions.AsNoTracking().SingleAsync(x => x.QrRequestId == qrId, ct);
        await using var gate = await locks.AcquireAsync(db, hint.OrderId, ct);
        var session = await Sessions.SingleAsync(x => x.Id == hint.Id, ct);
        await db.Entry(session).ReloadAsync(ct);
        RequireTerminal(await OrderAsync(session.OrderId, ct), session);
        var bankQueried = false;
        if (refresh && (session.Status is AcbSessionStatus.Pending or AcbSessionStatus.Creating ||
            (manual && session.Status == AcbSessionStatus.ReviewRequired)) &&
            (!session.LastRetrievedAtUtc.HasValue || session.LastRetrievedAtUtc < DateTime.UtcNow.AddSeconds(manual ? -1 : -8)))
        {
            await RetrieveLockedAsync(session, ct, manual ? AcbConfirmationSource.ManualCheck : AcbConfirmationSource.ScheduledCheck);
            bankQueried = true;
        }
        return new { qrId, session.OrderId, status = session.Status.ToString(), message = session.ReviewReason,
            session.LastRetrievedAtUtc, bankQueried };

    }

    public async Task<object> CompleteAsync(int qrId, CancellationToken ct)
    {
        var hint = await Sessions.AsNoTracking().SingleAsync(x => x.QrRequestId == qrId, ct);
        await using var gate = await locks.AcquireAsync(db, hint.OrderId, ct);
        var session = await Sessions.SingleAsync(x => x.Id == hint.Id, ct);
        await db.Entry(session).ReloadAsync(ct);
        var order = await OrderAsync(session.OrderId, ct);
        RequireTerminal(order, session);
        if (session.Status is not (AcbSessionStatus.Received or AcbSessionStatus.Completed))
            throw new InvalidOperationException(session.ReviewReason ?? "ACB chưa xác nhận đủ tiền.");
        var transactions = await db.Set<AcbPaymentTransaction>().Where(x => x.StoreId == StoreId && x.SessionId == session.Id).ToListAsync(ct);
        var reason = AcbPaymentPolicy.ReviewReason(session, order, transactions);
        if (reason != null)
        {
            session.Status = AcbSessionStatus.ReviewRequired; session.ReviewReason = reason;
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException(reason);
        }
        if (!session.PaymentId.HasValue)
        {
            // Save payment and its bank linkage together. A failed finalize may then be retried without adding money twice.
            var payment = new OrderPayment { StoreId = StoreId, OrderId = order.Id, Method = PaymentMethod.BankTransfer,
                Amount = session.Amount, Provider = "ACB", ReferenceCode = session.ProviderOrderId };
            db.OrderPayments.Add(payment);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            session.PaymentId = payment.Id;
            order.PaidTotal = order.Payments.Where(x => !x.IsDeleted).Sum(x => x.Amount);
            order.BalanceDue = Math.Max(0, order.GrandTotal - order.PaidTotal);
            order.ChangeDue = Math.Max(0, order.PaidTotal - order.GrandTotal);
            order.PaymentStatus = order.BalanceDue > 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Paid;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await FinishQrPaymentAsync(order, session.QrRequestId, session.Amount, ct);
    }

    public async Task<object> PendingTerminalAsync(CancellationToken ct)
    {
        if (runtime.StoreId != StoreId || runtime.TerminalId is not > 0) return Array.Empty<object>();
        return await Sessions.Where(x => x.TerminalId == runtime.TerminalId &&
            (x.Status == AcbSessionStatus.Pending || x.Status == AcbSessionStatus.Creating ||
             (x.Status == AcbSessionStatus.Received && (x.PaymentId == null || db.Orders.Any(o => o.Id == x.OrderId && o.BalanceDue <= 0))) ||
             (x.Status == AcbSessionStatus.Completed && x.PrintClaimedAtUtc == null)))
            .Where(x => db.POSShifts.Any(s => s.Id == x.ShiftId && s.StoreId == StoreId && s.Status == POSShiftStatus.Open))
            .OrderBy(x => x.Id).Take(20).Select(x => new { qrId = x.QrRequestId, x.OrderId }).ToListAsync(ct);
    }

    public async Task<object> LookupAsync(int orderId, bool refresh, CancellationToken ct)
    {
        var order = await OrderAsync(orderId, ct);
        await using var gate = await locks.AcquireAsync(db, orderId, ct);
        var sessions = await Sessions.Where(x => x.OrderId == orderId).OrderByDescending(x => x.Id).ToListAsync(ct);
        foreach (var session in sessions)
            if (refresh) await RetrieveLockedAsync(session, ct, AcbConfirmationSource.InvoiceLookup);
        var ids = sessions.Select(x => x.Id).ToList();
        var transactions = await db.Set<AcbPaymentTransaction>().Where(x => x.StoreId == StoreId && ids.Contains(x.SessionId)).ToListAsync(ct);
        var qrIds = sessions.Select(x => x.QrRequestId).ToList();
        var otherQrs = await db.PosPaymentQrRequests.AsNoTracking().Include(x => x.BankAccount)
            .Where(x => x.StoreId == StoreId && x.OrderId == orderId && !qrIds.Contains(x.Id))
            .OrderByDescending(x => x.Id)
            .ToListAsync(ct);
        var payments = order.Payments.Where(p => !p.IsDeleted && p.Method == PaymentMethod.BankTransfer).ToList();
        var actorIds = sessions.Select(x => x.ConfirmedByUserId).Concat(otherQrs.Select(x => x.ManualConfirmedByUserId))
            .Concat(payments.Select(x => x.CreatedBy)).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        // Only names referenced by this store's order are read; credentials are never projected.
        var users = await db.Set<User>().AsNoTracking().Where(x => actorIds.Contains(x.Id))
            .Select(x => new { x.Id, Name = x.FullName ?? x.UserName }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var confirmationBySession = sessions.ToDictionary(x => x.Id,
            x => AcbConfirmationAudit.ForSession(x, users, payments.Any(p => p.Id == x.PaymentId)));
        return new
        {
            order = new { order.Id, order.OrderNumber, order.GrandTotal, order.PaidTotal, order.BalanceDue },
            payments = payments
                .OrderByDescending(p => p.PaidAtUtc).Select(p => new
                {
                    p.Id, p.Amount, p.ReferenceCode, p.Provider, p.PaidAtUtc,
                    automatic = sessions.Any(s => s.PaymentId == p.Id && s.ConfirmationSource != AcbConfirmationSource.OfflineManual),
                    confirmation = sessions.FirstOrDefault(s => s.PaymentId == p.Id) is { } session
                        ? confirmationBySession[session.Id]
                        : AcbConfirmationAudit.ForManual(otherQrs.FirstOrDefault(q => q.PaymentId == p.Id), p, users)
                }).ToList(),
            otherQrs = otherQrs.Select(x => new { qrId = x.Id, x.RequestCode, x.Amount, x.Content, x.CreatedAtUtc,
                status = x.Status.ToString(), x.ManualConfirmedAtUtc, bankName = x.BankAccount.BankName,
                confirmation = AcbConfirmationAudit.ForManual(x, payments.FirstOrDefault(p => p.Id == x.PaymentId), users) }).ToList(),
            sessions = sessions.Select(x => new
        {
            qrId = x.QrRequestId, x.OrderId, x.ProviderOrderId, x.Amount, x.CreatedAtUtc,
            status = x.Status.ToString(), x.ReviewReason, x.LastRetrievedAtUtc, x.ShiftId, x.TerminalId,
            confirmation = confirmationBySession[x.Id],
            canCancel = runtime.StoreId == StoreId && runtime.TerminalId == x.TerminalId &&
                (x.Status == AcbSessionStatus.Pending || x.Status == AcbSessionStatus.Creating),
            transactions = transactions.Where(t => t.SessionId == x.Id).Select(t => new
            { t.TransactionNumber, t.Amount, t.Status, t.Content, t.PostedAt })
        }).ToList()
        };
    }
}

public sealed record AcbCallbackProcessingResult(bool AwaitingBank, bool Unmatched, bool NeedsReview = false);
