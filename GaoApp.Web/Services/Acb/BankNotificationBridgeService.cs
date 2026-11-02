using System.Globalization;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Common.POS;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

public sealed record BankNotificationUpload(string SourcePackage, string Content, long PostedAtEpochMs);
public sealed record BankNotificationSuggestion(int ReceiptId, int WaitId, int QrId, int OrderId,
    decimal Amount, string? SenderName, string TransactionCode, DateTime PostedAtUtc, bool Automatic,
    string Content);

public sealed partial class BankNotificationBridgeService(AppDbContext db, IPOSRuntimeContextAccessor runtime,
    IAcbOrderLockProvider locks, AcbPaymentService payments, IPosRealtimeNotifier notifier,
    BankNotificationMonitor? monitor = null, IConfiguration? configuration = null)
{
    private int StoreId => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new InvalidOperationException("Chưa xác định cửa hàng.");
    public async Task<bool> ArmAsync(int qrId, CancellationToken ct)
    {
        var qr = await db.PosPaymentQrRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == qrId && x.StoreId == StoreId, ct);
        if (qr == null || qr.ConfirmMode != BankQrConfirmMode.Manual || qr.QrRenderMode != BankQrRenderMode.LocalEmvQr || qr.Status != PosPaymentQrStatus.Pending || qr.PaymentId != null) return false;
        if (!await db.Set<BankNotificationDevice>().AnyAsync(x => x.StoreId == StoreId && x.BankAccountId == qr.BankAccountId && x.Enabled, ct)) return false;
        var bank = await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.StoreId == StoreId && x.Id == qr.BankAccountId, ct);
        var order = await db.Orders.AsNoTracking().Include(x => x.POSShift).Include(x => x.Payments)
            .Include(x => x.Lines).ThenInclude(x => x.Variant).SingleAsync(x => x.Id == qr.OrderId && x.StoreId == StoreId, ct);
        if (order.Status != OrderStatus.Draft || order.POSShift.TerminalId != runtime.TerminalId || order.POSShift.Status != POSShiftStatus.Open)
            throw new InvalidOperationException("Đơn không thuộc quầy/ca đang chờ.");
        await using var gate = await locks.AcquireAsync(db, 0, ct);
        if (await db.Set<BankNotificationWait>().AnyAsync(x => x.StoreId == StoreId && x.QrId == qrId, ct)) return true;
        db.Add(new BankNotificationWait { StoreId = StoreId, QrId = qrId, OrderId = order.Id, BankAccountId = qr.BankAccountId,
            BankAccountFingerprint = BankNotificationParser.AccountFingerprint(bank),
            TerminalId = order.POSShift.TerminalId, ShiftId = order.POSShiftId, Amount = qr.Amount,
            ExpiresAtUtc = qr.ExpireAtUtc, CartFingerprint = AcbPaymentPolicy.Fingerprint(order) });
        await db.SaveChangesAsync(ct);
        return true;
    }
    public Task<int?> AcceptAsync(BankNotificationDevice device, BankNotificationUpload upload, CancellationToken ct)
        => AcceptCoreAsync(device, upload, null, ct);
    public Task<int?> AcceptAuthenticatedAsync(GaoApp.Web.Security.AuthenticatedBankNotification authenticated, BankNotificationUpload upload, CancellationToken ct)
        => AcceptCoreAsync(authenticated.Device, upload, authenticated, ct);
    private async Task<int?> AcceptCoreAsync(BankNotificationDevice device, BankNotificationUpload upload, GaoApp.Web.Security.AuthenticatedBankNotification? authenticated, CancellationToken ct)
    {
        if (device.StoreId != StoreId || !device.Enabled || upload.SourcePackage != "mobile.acb.com.vn") throw new InvalidOperationException("Thiết bị hoặc nguồn thông báo không hợp lệ.");
        var bank = await db.StoreBankAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == device.BankAccountId && x.StoreId == StoreId && x.IsActive && x.BankCode == "ACB" &&
            x.ConfirmMode == BankQrConfirmMode.Manual && x.QrRenderMode == BankQrRenderMode.LocalEmvQr, ct);
        if (bank == null) throw new InvalidOperationException("Tài khoản nhận đang tắt hoặc không dùng QR thủ công.");
        var accountFingerprint = BankNotificationParser.AccountFingerprint(bank);
        if (device.BankAccountFingerprint != accountFingerprint) throw new InvalidOperationException("Tài khoản nhận đã thay đổi. Đăng ký lại điện thoại cho tài khoản đúng.");
        var parsed = BankNotificationParser.Parse(upload.Content);
        if (parsed == null) return null; // Do not persist OTP, redacted or ambiguous content.
        DateTime posted;
        try { posted = DateTimeOffset.FromUnixTimeMilliseconds(upload.PostedAtEpochMs).UtcDateTime; }
        catch (ArgumentOutOfRangeException) { throw new InvalidOperationException("Thời gian thông báo không hợp lệ."); }
        if (posted > DateTime.UtcNow.AddMinutes(2) || posted < DateTime.UtcNow.AddDays(-2)) throw new InvalidOperationException("Thông báo nằm ngoài thời gian tiếp nhận.");
        var day = new DateTimeOffset(parsed.BankOccurredAtUtc ?? posted, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(7)).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var key = BankNotificationParser.Hash(accountFingerprint + "|" + (parsed.TransactionCode.StartsWith("MGD:", StringComparison.Ordinal) ? day + "|" : "") + parsed.TransactionCode);
        await using var gate = await locks.AcquireAsync(db, 0, ct);
        await using var acceptanceTx = authenticated is not null ? await db.Database.BeginTransactionAsync(ct) : null;
        if (authenticated is not null)
        {
            if (authenticated.StoreId != StoreId || authenticated.DeviceId != device.Id || authenticated.BankAccountId != bank.Id)
                throw new InvalidOperationException("Phạm vi xác thực thiết bị không khớp.");
            // The native attestation freezes global physical-account uncertainty. Take the
            // same account lock as manual confirmation before either path locks SQL rows.
            await GaoApp.Infrastructure.Services.Banking.GlobalBankReceiptLedger.LockAsync(db,
                "GaoApp:BankAccountEvidence:" + GaoApp.Infrastructure.Services.Banking.GlobalBankReceiptLedger.AccountIdentity(bank.BankCode, bank.AccountNumber), ct);
            var freshDevice = await db.Set<BankNotificationDevice>().FromSqlInterpolated($"SELECT * FROM [BankNotificationDevices] WITH(UPDLOCK,HOLDLOCK) WHERE [StoreId]={StoreId} AND [Id]={device.Id}").AsNoTracking().SingleAsync(ct);
            var freshBank = await db.StoreBankAccounts.FromSqlInterpolated($"SELECT * FROM [StoreBankAccounts] WITH(UPDLOCK,HOLDLOCK) WHERE [StoreId]={StoreId} AND [Id]={bank.Id}").AsNoTracking().SingleAsync(ct);
            if (!freshDevice.Enabled || freshDevice.TokenHash != authenticated.TokenHash || freshDevice.BankAccountId != authenticated.BankAccountId ||
                freshDevice.BankAccountFingerprint != authenticated.Fingerprint || Convert.ToBase64String(freshBank.RowVersion) != authenticated.AccountVersion ||
                !freshBank.IsActive || BankNotificationParser.AccountFingerprint(freshBank) != authenticated.Fingerprint)
                throw new InvalidOperationException("Thiết bị/tài khoản đã đổi sau khi xác thực; gửi lại với cấu hình đúng.");
            bank = freshBank;
        }
        await db.Entry(device).ReloadAsync(ct);
        if (!device.Enabled || device.BankAccountFingerprint != accountFingerprint) throw new InvalidOperationException("Thiết bị đã ngừng nhận thông báo cho tài khoản này.");
        var receipt = new BankNotificationReceipt { StoreId = StoreId, DeviceId = device.Id, BankAccountId = bank.Id,
            EvidenceKey = key, PayloadHash = BankNotificationParser.Hash(BankNotificationParser.Fold(upload.Content)), Content = upload.Content,
            Amount = parsed.Amount, OrderId = parsed.OrderId, QrCode = parsed.QrCode, SenderName = parsed.SenderName,
            TransactionCode = parsed.TransactionCode, PostedAtUtc = posted };
        var old = (await BankNotificationEvidence.RelatedAsync(db, receipt, ct))
            .OrderByDescending(x => x.PaymentId != null || x.AppliedWaitId != null).ThenBy(x => x.Id).FirstOrDefault();
        if (old != null)
        {
            var original = BankNotificationParser.Parse(old.Content);
            var oldOrder = original?.OrderId ?? old.OrderId;
            var oldQr = original?.QrCode ?? old.QrCode;
            if (old.Amount != parsed.Amount || oldOrder != null && parsed.OrderId != null && oldOrder != parsed.OrderId ||
                oldQr != null && parsed.QrCode != null && !string.Equals(oldQr, parsed.QrCode, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Mã giao dịch đã nhận với dữ liệu khác; cần đối soát.");
            await LinkAdminReceiptAsync(old, ct);
            if (acceptanceTx is not null) await acceptanceTx.CommitAsync(ct);
            return old.Id; // Lost ACK, another phone or a richer notification never creates new money.
        }
        db.Add(receipt); device.LastSeenAtUtc = DateTime.UtcNow;
        if(parsed.QrCode is not null&&System.Text.RegularExpressions.Regex.IsMatch(parsed.QrCode,"^W[0-9]{12}$"))receipt.ConfirmationSource="StorefrontReceiptReview";
        var hadManualAmbiguity = authenticated is not null
            ? await BankNotificationEvidence.HasNativeManualAmbiguityAsync(db, receipt,
                GaoApp.Infrastructure.Services.Banking.GlobalBankReceiptLedger.AccountIdentity(bank.BankCode, bank.AccountNumber), ct)
            : await HasManualAmbiguityAsync(receipt, ct);
        if (hadManualAmbiguity) receipt.ConfirmationSource = "LateManualReview";
        await db.SaveChangesAsync(ct);
        if (authenticated is not null)
        {
            var grants = await db.Set<GaoApp.Domain.Storefront.StorefrontBankCollectionAuthority>().FromSqlInterpolated($"SELECT * FROM [StorefrontBankCollectionAuthorities] WITH(HOLDLOCK) WHERE [StoreId]={StoreId} AND [BankAccountId]={bank.Id} AND [IsActive]=1")
                .AsNoTracking().OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
            var sealedGrants = grants.Count <= 1000 ? grants.Select(x => new GaoApp.Application.Storefront.BankCollectionAuthorityProof(x.Id, x.BankAccountId,
                x.ReceivingLegalEntityId, x.SourceLegalEntityId, Convert.ToBase64String(x.RowVersion))).ToList() : [];
            db.Add(new GaoApp.Domain.Storefront.StorefrontNotificationAttestation { StoreId = StoreId, BankNotificationReceiptId = receipt.Id,
                DeviceId = device.Id, BankAccountId = bank.Id, AccountIdentity = accountFingerprint, AccountVersion = authenticated.AccountVersion,
                HadManualAmbiguity = hadManualAmbiguity,
                SourcePackage = upload.SourcePackage, PayloadHash = receipt.PayloadHash, EvidenceKey = receipt.EvidenceKey, NativeTransactionId = parsed.TransactionCode,
                BankOccurredAtUtc = parsed.BankOccurredAtUtc, AuthenticatedAtUtc = authenticated.AtUtc,
                AccountSnapshotJson = System.Text.Json.JsonSerializer.Serialize(GaoApp.Infrastructure.Services.Banking.GlobalBankReceiptLedger.AccountSnapshot(bank)),
                CollectionAuthoritySnapshotJson = System.Text.Json.JsonSerializer.Serialize(sealedGrants) });
            await db.SaveChangesAsync(ct);
        }
        await LinkAdminReceiptAsync(receipt, ct);
        if (acceptanceTx is not null) await acceptanceTx.CommitAsync(ct);
        foreach (var candidate in await CandidatesAsync(receipt, ct))
            await notifier.NotifyTerminalAsync(StoreId, candidate.Wait.TerminalId.ToString(CultureInfo.InvariantCulture), "bank_notification_available", candidate.Wait.OrderId, ct: ct);
        return receipt.Id;
    }
    private sealed record Candidate(BankNotificationWait Wait, bool Automatic);
    private async Task<List<Candidate>> CandidatesAsync(BankNotificationReceipt receipt, CancellationToken ct)
    {
        if(receipt.ConfirmationSource=="StorefrontReceiptReview"||BankNotificationParser.Parse(receipt.Content)?.QrCode is string webRef&&System.Text.RegularExpressions.Regex.IsMatch(webRef,"^W[0-9]{12}$"))return [];
        if (receipt.PaymentId != null || receipt.AppliedWaitId != null || await BankNotificationEvidence.ClaimedAsync(db, receipt, null, ct)) return [];
        if (receipt.ConfirmationSource == "LateManualReview" || await HasManualAmbiguityAsync(receipt, ct)) return [];
        if (!await db.StoreBankAccounts.AnyAsync(b => b.StoreId == StoreId && b.Id == receipt.BankAccountId && b.IsActive && b.BankCode == "ACB" &&
            b.ConfirmMode == BankQrConfirmMode.Manual && b.QrRenderMode == BankQrRenderMode.LocalEmvQr, ct)) return [];
        var bank = await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.StoreId == StoreId && x.Id == receipt.BankAccountId, ct);
        var source = await db.Set<BankNotificationDevice>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == receipt.DeviceId, ct);
        var fingerprint = BankNotificationParser.AccountFingerprint(bank);
        if (source?.BankAccountFingerprint != fingerprint) return [];
        var original = BankNotificationParser.Parse(receipt.Content);
        var orderId = original?.OrderId ?? receipt.OrderId;
        var qrCode = original?.QrCode ?? receipt.QrCode;
        var occurred = BankNotificationEvidence.OccurredAt(receipt);
        var rows = await (from w in db.Set<BankNotificationWait>().AsNoTracking()
                          join q in db.PosPaymentQrRequests.AsNoTracking() on w.QrId equals q.Id
                          where w.StoreId == StoreId && w.BankAccountId == receipt.BankAccountId && w.Amount == receipt.Amount &&
                          w.BankAccountFingerprint == fingerprint &&
                          q.CreatedAtUtc <= occurred.AddSeconds(5) && w.ExpiresAtUtc >= occurred.AddSeconds(-5) &&
                          q.StoreId == StoreId && q.Status == PosPaymentQrStatus.Pending && q.PaymentId == null &&
                          q.NotificationConfirmationSource != ManualReviewSource &&
                          q.NotificationConfirmationSource != AdminApprovalSource && q.NotificationConfirmationSource != AdminReceiptApprovalSource &&
                          q.ConfirmMode == BankQrConfirmMode.Manual && q.QrRenderMode == BankQrRenderMode.LocalEmvQr &&
                          q.Amount == w.Amount && q.BankAccountId == w.BankAccountId &&
                          !db.Set<BankNotificationDismissal>().Any(d => d.StoreId == StoreId && d.ReceiptId == receipt.Id && d.WaitId == w.Id)
                          select new { Wait = w, Qr = q }).Take(50).ToListAsync(ct);
        var candidates = new List<Candidate>();
        foreach (var row in rows)
        {
            if (orderId != null && orderId != row.Wait.OrderId) continue;
            if (qrCode != null && !string.Equals(qrCode, row.Qr.RequestCode, StringComparison.OrdinalIgnoreCase)) continue;
            var order = await db.Orders.AsNoTracking().Include(x => x.POSShift).Include(x => x.Payments)
                .Include(x => x.Lines).ThenInclude(x => x.Variant).SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == row.Wait.OrderId, ct);
            if (order == null || order.Status != OrderStatus.Draft || order.POSShiftId != row.Wait.ShiftId ||
                order.POSShift.TerminalId != row.Wait.TerminalId || order.POSShift.Status != POSShiftStatus.Open ||
                !AcbPaymentPolicy.MatchesFingerprint(order, row.Wait.CartFingerprint) || AcbPaymentPolicy.Balance(order) <= 0) continue;
            candidates.Add(new(row.Wait, orderId == row.Wait.OrderId));
        }
        // POS without RequestCode is sufficient when its one pending request matches.
        if (candidates.Count > 1) candidates = candidates.Select(c => c with { Automatic = false }).ToList();
        return candidates;
    }
    public async Task<IReadOnlyList<BankNotificationSuggestion>> PendingAsync(CancellationToken ct)
    {
        var ids = await db.Set<BankNotificationDevice>().Where(x => x.StoreId == StoreId && x.Enabled).Select(x => x.BankAccountId).Distinct().ToListAsync(ct);
        var receipts = await db.Set<BankNotificationReceipt>().AsNoTracking().Where(x => x.StoreId == StoreId && x.PaymentId == null && ids.Contains(x.BankAccountId) && x.PostedAtUtc >= DateTime.UtcNow.AddDays(-2))
            .OrderByDescending(x => x.Id).Take(60).ToListAsync(ct);
        var result = new List<BankNotificationSuggestion>();
        foreach (var receipt in receipts)
            foreach (var c in await CandidatesAsync(receipt, ct))
                if (c.Wait.TerminalId == runtime.TerminalId)
                    result.Add(new(receipt.Id, c.Wait.Id, c.Wait.QrId, c.Wait.OrderId, receipt.Amount, receipt.SenderName, receipt.TransactionCode, DateTime.SpecifyKind(receipt.PostedAtUtc, DateTimeKind.Utc), c.Automatic, receipt.Content));
        return result;
    }
    public async Task<object> ConfirmAsync(int receiptId, int waitId, bool automatic, CancellationToken ct)
    {
        await using var gate = await locks.AcquireAsync(db, 0, ct);
        var receipt = await db.Set<BankNotificationReceipt>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == receiptId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy thông báo.");
        await db.Entry(receipt).ReloadAsync(ct);
        var wait = await db.Set<BankNotificationWait>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == waitId && x.TerminalId == runtime.TerminalId, ct)
            ?? throw new InvalidOperationException("Yêu cầu không thuộc quầy này.");
        if (!await db.Set<BankNotificationDevice>().AnyAsync(x => x.StoreId == StoreId && x.BankAccountId == wait.BankAccountId && x.Enabled, ct))
            throw new InvalidOperationException("Đọc thông báo cho tài khoản này đang tắt.");
        if (await BankNotificationEvidence.ClaimedAsync(db, receipt, wait.Id, ct))
            throw new InvalidOperationException("Khoản tiền đã được xác nhận hoặc dành cho một đơn khác.");
        if (receipt.PaymentId != null)
        {
            if (receipt.AppliedWaitId != wait.Id) throw new InvalidOperationException("Khoản tiền đã được xác nhận cho đơn khác.");
            return await payments.ConfirmManualQrAsync(wait.QrId, ct, receipt, wait);
        }
        var candidate = (await CandidatesAsync(receipt, ct)).SingleOrDefault(x => x.Wait.Id == wait.Id)
            ?? throw new InvalidOperationException("Đơn hoặc khoản chờ đã thay đổi. Kiểm tra thủ công.");
        if (automatic && !candidate.Automatic) throw new InvalidOperationException("Khoản này cần thu ngân chọn Đúng để xác nhận.");
        receipt.ConfirmationSource = automatic ? "NotificationAuto" : "NotificationCashier";
        return await payments.ConfirmManualQrAsync(wait.QrId, ct, receipt, wait);
    }
    public async Task DismissAsync(int receiptId, int waitId, CancellationToken ct)
    {
        await using var gate = await locks.AcquireAsync(db, 0, ct);
        var wait = await db.Set<BankNotificationWait>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == waitId && x.TerminalId == runtime.TerminalId, ct);
        var receipt = await db.Set<BankNotificationReceipt>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == receiptId, ct);
        if (wait == null || receipt == null || receipt.BankAccountId != wait.BankAccountId || receipt.Amount != wait.Amount)
            throw new InvalidOperationException("Thông báo không thuộc khoản chờ của quầy/cửa hàng.");
        await PauseCoreAsync(wait.QrId, true, ct);
        if (!await db.Set<BankNotificationDismissal>().AnyAsync(x => x.StoreId == StoreId && x.ReceiptId == receiptId && x.WaitId == waitId, ct))
        {
            db.Add(new BankNotificationDismissal { StoreId = StoreId, ReceiptId = receiptId, WaitId = waitId });
            await db.SaveChangesAsync(ct);
        }
    }
}
