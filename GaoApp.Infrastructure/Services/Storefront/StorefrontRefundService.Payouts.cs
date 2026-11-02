using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontRefundService
{
    private sealed record PayoutPlan(int? CashId, IReadOnlySet<int> TreasuryIds, int? BankId, string? AccountIdentity, string? AccountVersion);
    private sealed record PayoutSource(StorefrontRefundPayoutSourceKind Kind, int Id, int? CashId, int? TreasuryId,
        string Fund, decimal Amount, DateTime AtUtc, string Version, string? CashVersion, string? TreasuryVersion, string Canonical, string Label);
    private sealed record PayoutAttestation(DateTime AtUtc, int? BankAccountId, string Method, string ExternalKey, string Snapshot, string ProtectedContext);
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static string Version(byte[]? bytes)
    {
        if (bytes is not { Length: 8 }) throw new ConflictAppException("Chứng từ thiếu phiên bản nguồn hợp lệ.");
        return Convert.ToBase64String(bytes);
    }
    private static bool WholePositive(decimal amount) => amount > 0 && amount < StorefrontValues.MoneyLimit && decimal.Truncate(amount) == amount;
    private static void ValidatePayoutRequest(StorefrontRefundExecutionRequest request)
    {
        if (!Enum.IsDefined(request.SourceKind) || request.SourceId <= 0 || request.RefundId <= 0 || request.PayerLegalEntityId <= 0 ||
            string.IsNullOrWhiteSpace(request.ExternalTransactionId) || request.ExternalTransactionId.Length > 100 ||
            request.ExternalTransactionId != request.ExternalTransactionId.Trim() || request.ExternalTransactionId.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(request.EvidenceReference) || request.EvidenceReference.Length > 1000)
            throw new ValidationAppException("Cần đúng hồ sơ, nguồn chi, chủ thể, mã giao dịch và chứng cứ đã thực trả.");
        byte[] bytes; try { bytes = Convert.FromBase64String(request.SourceVersion ?? ""); }
        catch (FormatException) { throw new ValidationAppException("Phiên bản chứng từ thực chi không hợp lệ."); }
        if (bytes.Length != 8 || Convert.ToBase64String(bytes) != request.SourceVersion)
            throw new ValidationAppException("Phiên bản chứng từ thực chi không hợp lệ.");
        if (request.PayerBankAccountId.HasValue)
        {
            try { bytes = Convert.FromBase64String(request.PayerBankAccountVersion ?? ""); }
            catch (FormatException) { throw new ValidationAppException("Phiên bản tài khoản thực chi không hợp lệ; tải lại hồ sơ."); }
            if (bytes.Length != 8 || Convert.ToBase64String(bytes) != request.PayerBankAccountVersion)
                throw new ValidationAppException("Cần phiên bản tài khoản đã chọn; tải lại hồ sơ trước khi xác nhận.");
        }
        else if (request.PayerBankAccountVersion is not null)
            throw new ValidationAppException("Phiếu chi tiền mặt không được gắn phiên bản tài khoản ngân hàng.");
    }

    private async Task<PayoutPlan> LockPayout(int orderId, StorefrontRefundExecutionRequest request, CancellationToken ct)
    {
        int? cashId = request.SourceKind == StorefrontRefundPayoutSourceKind.CashOut ? request.SourceId : null;
        var treasuryIds = new HashSet<int>();
        if (request.SourceKind == StorefrontRefundPayoutSourceKind.TreasuryDebit)
        {
            treasuryIds.Add(request.SourceId);
            cashId = await db.TreasuryEntries.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == Store && x.Id == request.SourceId)
                .Select(x => x.POSShiftCashTransactionId).SingleOrDefaultAsync(ct);
        }
        if (cashId.HasValue)
        {
            foreach (var id in await db.TreasuryEntries.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == Store && x.POSShiftCashTransactionId == cashId)
                .Select(x => x.Id).ToListAsync(ct)) treasuryIds.Add(id);
        }
        var bank = request.PayerBankAccountId.HasValue ? await db.StoreBankAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == request.PayerBankAccountId, ct) : null;
        var identity = bank is null ? null : BankPhysicalAccountIdentity.Create(bank.BankCode, bank.AccountNumber);
        await StorefrontOrderProofLocks.LockAsync(db, Store, orderId, ct, incomingCashId: cashId,
            additionalAccounts: identity is null ? [] : [identity], additionalTreasuryIds: treasuryIds);
        foreach (var id in treasuryIds.Order())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [TreasuryEntries] WITH(UPDLOCK,HOLDLOCK) WHERE [StoreId]={Store} AND [Id]={id};", ct);
        if (bank is not null)
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [StoreBankAccounts] WITH(HOLDLOCK) WHERE [StoreId]={Store} AND [Id]={bank.Id};", ct);
        return new(cashId, treasuryIds, bank?.Id, identity, bank is null ? null : Version(bank.RowVersion));
    }
    private async Task CheckTreasury(TreasuryEntry entry, CancellationToken ct, PayoutPlan? plan)
    {
        if (plan is not null && !plan.TreasuryIds.Contains(entry.Id))
            throw new StorefrontRetryableCommandException("Liên kết phiếu chi vừa thay đổi; thử lại đúng mã yêu cầu để khóa lại đủ nguồn.");
        Need(!entry.IsDeleted && entry.Amount < 0 && WholePositive(-entry.Amount) && (entry.Fund is "cash" or "bank") &&
            entry.TargetFund is null && entry.OperatingExpenseId is null && entry.PurchasePayableId is null &&
            entry.ReversalOfId is null && entry.ReversedAtUtc is null, "Phiếu phải là khoản thực chi chưa đảo, không phải chuyển quỹ, chi phí hoặc khoản phải trả khác.");
        Need(!await db.TreasuryEntries.IgnoreQueryFilters().AnyAsync(x => x.StoreId == Store && x.ReversalOfId == entry.Id, ct), "Phiếu chi đã có chứng từ đảo; không xác nhận hoàn từ nguồn này.");
    }
    private async Task<PayoutSource> ReadCash(int id, CancellationToken ct, PayoutPlan? plan)
    {
        if (plan is not null && plan.CashId != id) throw new StorefrontRetryableCommandException("Nguồn két vừa đổi liên kết; thử lại đúng mã yêu cầu cũ.");
        var cash = await db.POSShiftCashTransactions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == id, ct)
            ?? throw new NotFoundAppException("Không tìm thấy phiếu thực chi của cửa hàng.");
        Need(!cash.IsDeleted && cash.Type == POSShiftCashTransactionType.CashOut && WholePositive(cash.Amount) &&
            cash.CustomerDebtReceiptId is null && cash.CustomerDepositEntryId is null &&
            cash.Reason is not ("Thu công nợ khách hàng" or "Nhận cọc khách hàng" or "Hoàn cọc khách hàng"),
            "Cần phiếu CashOut thực tế; phiếu thu, tiền cọc hoặc thu nợ không phải tiền hoàn.");
        var shift = await db.POSShifts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == cash.POSShiftId, ct);
        Need(shift is not null && !shift.IsDeleted && await db.POSTerminals.IgnoreQueryFilters().AnyAsync(x => x.StoreId == Store && x.Id == shift.TerminalId && !x.IsDeleted, ct) &&
            cash.CreatedAtUtc >= shift.OpenedAtUtc && (!shift.ClosedAtUtc.HasValue || cash.CreatedAtUtc <= shift.ClosedAtUtc.Value) &&
            cash.CreatedByUserId > 0 && await db.Users.IgnoreQueryFilters().AnyAsync(x => x.Id == cash.CreatedByUserId, ct), "Phiếu chi chưa có nguồn ca, quầy và người ghi thực tế phù hợp.");
        Need(!await db.Set<POSCashAdjustmentRequest>().IgnoreQueryFilters().AnyAsync(x => x.StoreId == Store && x.TransactionId == id && x.Status == POSCashAdjustmentStatus.Pending, ct), "Phiếu chi đang có yêu cầu điều chỉnh; đối soát trước khi xác nhận hoàn.");
        var linked = await db.TreasuryEntries.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == Store && x.POSShiftCashTransactionId == id).Take(2).ToListAsync(ct);
        Need(linked.Count <= 1, "Nguồn chi có nhiều liên kết quỹ không nhất quán.");
        var treasury = linked.SingleOrDefault();
        if (treasury is not null)
        {
            await CheckTreasury(treasury, ct, plan);
            Need(treasury.IsVoucherLink && treasury.Fund == "cash" && treasury.Amount == -cash.Amount, "Liên kết phiếu quỹ không khớp khoản chi két gốc.");
        }
        return new(StorefrontRefundPayoutSourceKind.CashOut, id, id, treasury?.Id, "cash", cash.Amount, Utc(cash.CreatedAtUtc),
            Version(cash.RowVersion), Version(cash.RowVersion), treasury is null ? null : Version(treasury.RowVersion), "CASH:" + Store + ":" + id, "Phiếu chi két " + id);
    }
    private async Task<PayoutSource> ReadPayout(StorefrontRefundPayoutSourceKind kind, int id, CancellationToken ct, PayoutPlan? plan = null)
    {
        PayoutSource result;
        if (kind == StorefrontRefundPayoutSourceKind.CashOut) result = await ReadCash(id, ct, plan);
        else
        {
            Need(kind == StorefrontRefundPayoutSourceKind.TreasuryDebit, "Nguồn chi không được hỗ trợ.");
            var treasury = await db.TreasuryEntries.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == id, ct)
                ?? throw new NotFoundAppException("Không tìm thấy phiếu thực chi của cửa hàng.");
            await CheckTreasury(treasury, ct, plan);
            if (treasury.POSShiftCashTransactionId is int cashId)
            {
                var cash = await ReadCash(cashId, ct, plan);
                Need(cash.TreasuryId == id && treasury.IsVoucherLink, "Phiếu quỹ chưa gắn đúng phiếu chi két thực tế.");
                result = cash with { Kind = kind, Id = id, Version = Version(treasury.RowVersion), Label = "Phiếu quỹ " + id + " gắn " + cash.Label };
            }
            else
            {
                Need(!treasury.IsVoucherLink, "Phiếu phân loại két thiếu nguồn chi gốc.");
                result = new(kind, id, null, id, treasury.Fund, -treasury.Amount, Utc(treasury.OccurredAtUtc),
                    Version(treasury.RowVersion), null, Version(treasury.RowVersion), "TREASURY:" + Store + ":" + id, "Phiếu chi quỹ " + id);
            }
        }
        Need(!await db.Set<StorefrontRefundEvidence>().IgnoreQueryFilters().AnyAsync(x =>
            result.CashId.HasValue && x.POSShiftCashTransactionId == result.CashId || result.TreasuryId.HasValue && x.TreasuryEntryId == result.TreasuryId, ct), "Chứng từ thực chi hoặc nguồn két tương đương đã được dùng để xác nhận hoàn.");
        Need(!await db.Set<StorefrontCodReceipt>().IgnoreQueryFilters().AnyAsync(x =>
            result.CashId.HasValue && x.POSShiftCashTransactionId == result.CashId || result.TreasuryId.HasValue && x.TreasuryEntryId == result.TreasuryId, ct), "Nguồn đang là chứng từ thu COD; không dùng lại làm khoản chi hoàn.");
        return result;
    }

    public async Task<IReadOnlyList<StorefrontRefundPayoutSource>> PayoutSourcesAsync(int orderId, int refundId, CancellationToken ct = default)
    {
        await Guard(StorefrontPermissions.ConfirmRefund, ct); await Guard(PermissionCodes.Report.Treasury.Manage, ct);
        var order = await Order(orderId, ct); var refund = await Refund(orderId, refundId, ct);
        var funding = await Funding(orderId, refundId, ct); var proof = await Source(order, funding, ct);
        if (refund.State != StorefrontRefundState.Approved || !proof.Eligible || await PhysicalBlock(order, ct) is not null) return [];
        var remaining = refund.Amount - await Executed(orderId, refundId, ct);
        var latest = Now.AddMinutes(2);
        if (remaining <= 0 || proof.EarliestPayoutAtUtc > latest) return [];
        var firstBusinessDay = proof.EarliestPayoutAtUtc.AddHours(7).Date.AddHours(-7);
        var lastBusinessDayEnd = latest.AddHours(7).Date.AddDays(1).AddHours(-7);
        var cash = db.POSShiftCashTransactions.AsNoTracking().Where(x => x.StoreId == Store && x.Type == POSShiftCashTransactionType.CashOut &&
            x.CreatedAtUtc >= proof.EarliestPayoutAtUtc && x.CreatedAtUtc <= latest &&
            x.Amount > 0 && x.Amount <= remaining && x.Amount < StorefrontValues.MoneyLimit && decimal.Truncate(x.Amount) == x.Amount &&
            x.CustomerDebtReceiptId == null && x.CustomerDepositEntryId == null &&
            x.Reason != "Thu công nợ khách hàng" && x.Reason != "Nhận cọc khách hàng" && x.Reason != "Hoàn cọc khách hàng" &&
            !db.Set<StorefrontRefundEvidence>().IgnoreQueryFilters().Any(e => e.POSShiftCashTransactionId == x.Id) &&
            !db.Set<StorefrontCodReceipt>().IgnoreQueryFilters().Any(e => e.POSShiftCashTransactionId == x.Id)).Select(x => x.Id);
        // Linked vouchers are represented by their one CashOut, never listed as a second asset.
        var treasury = db.TreasuryEntries.AsNoTracking().Where(x => x.StoreId == Store && x.Amount < 0 && -x.Amount <= remaining &&
            x.OccurredAtUtc >= firstBusinessDay && x.OccurredAtUtc < lastBusinessDayEnd &&
            -x.Amount < StorefrontValues.MoneyLimit && decimal.Truncate(x.Amount) == x.Amount && x.POSShiftCashTransactionId == null &&
            (x.Fund == "cash" || x.Fund == "bank") && x.TargetFund == null && x.OperatingExpenseId == null && x.PurchasePayableId == null &&
            x.ReversalOfId == null && x.ReversedAtUtc == null && !x.IsVoucherLink &&
            !db.TreasuryEntries.IgnoreQueryFilters().Any(r => r.StoreId == Store && r.ReversalOfId == x.Id) &&
            !db.Set<StorefrontRefundEvidence>().IgnoreQueryFilters().Any(e => e.TreasuryEntryId == x.Id) &&
            !db.Set<StorefrontCodReceipt>().IgnoreQueryFilters().Any(e => e.TreasuryEntryId == x.Id)).Select(x => x.Id);
        var results = new List<StorefrontRefundPayoutSource>();
        await AppendEligiblePayouts(cash, StorefrontRefundPayoutSourceKind.CashOut, proof, results, ct);
        await AppendEligiblePayouts(treasury, StorefrontRefundPayoutSourceKind.TreasuryDebit, proof, results, ct);
        return results;
    }

    private async Task AppendEligiblePayouts(IQueryable<int> candidates, StorefrontRefundPayoutSourceKind kind,
        StorefrontRefundSourceResult proof, List<StorefrontRefundPayoutSource> results, CancellationToken ct)
    {
        // Cap validated choices, not raw rows: old genuine debits must remain reachable behind
        // claimed, reversed or otherwise ineligible recent documents. IDs provide stable keyset pages.
        var count = 0; int? before = null;
        while (count < 100)
        {
            var page = before is int lastId ? candidates.Where(id => id < lastId) : candidates;
            var ids = await page.OrderByDescending(id => id).Take(100).ToListAsync(ct);
            if (ids.Count == 0) break;
            before = ids[^1];
            foreach (var id in ids)
            {
                try
                {
                    var source = await ReadPayout(kind, id, ct);
                    if (source.CashId.HasValue ? source.AtUtc < proof.EarliestPayoutAtUtc || source.AtUtc > Now.AddMinutes(2)
                        : source.AtUtc.AddHours(7).Date.AddDays(1).AddHours(-7) <= proof.EarliestPayoutAtUtc || proof.EarliestPayoutAtUtc > Now.AddMinutes(2)) continue;
                    results.Add(new(source.Kind, source.Id, source.Label, source.Fund, source.Amount, source.Version, source.AtUtc));
                    if (++count == 100) break;
                }
                catch (ConflictAppException) { /* Only validated unclaimed actual sources are offered. */ }
                catch (NotFoundAppException) { /* A concurrently removed candidate is not a substitute source. */ }
            }
            if (ids.Count < 100) break;
        }
    }

    private async Task<PayoutAttestation> AttestPayout(StorefrontRefundExecutionRequest request, PayoutSource source,
        StorefrontRefundSourceResult proof, PayoutPlan? plan, CancellationToken ct)
    {
        Need(proof.Owner is > 0 && proof.Owner == request.PayerLegalEntityId &&
            await db.LegalEntities.AnyAsync(x => x.StoreId == Store && x.Id == request.PayerLegalEntityId && x.IsActive, ct), "Chủ thể thực trả phải khớp chủ thể đã nhận tiền có chứng cứ; không suy từ kho hoặc ca.");
        StoreBankAccount? bank = null;
        var at = source.AtUtc;
        if (!source.CashId.HasValue)
        {
            Need(request.ExecutedAt.HasValue && request.ExecutedAt.Value != default, "Cần thời điểm thực chi đã xác minh; ngày phiếu quỹ không tự là giờ ngân hàng trả tiền.");
            at = request.ExecutedAt!.Value.UtcDateTime;
            Need(at.AddHours(7).Date == source.AtUtc.AddHours(7).Date, "Thời điểm thực trả không cùng ngày nghiệp vụ của phiếu quỹ đã chọn.");
        }
        Need(at >= proof.EarliestPayoutAtUtc && at <= Now.AddMinutes(2), "Thời điểm thực trả không khớp bằng chứng nhận tiền hoặc nằm trong tương lai.");
        if (source.Fund == "bank")
        {
            Need(request.PayerBankAccountId is > 0, "Cần chọn tài khoản đã thực chuyển tiền.");
            bank = await db.StoreBankAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == request.PayerBankAccountId && x.IsActive, ct);
            Need(bank is not null && !string.IsNullOrWhiteSpace(bank.BankCode) && !string.IsNullOrWhiteSpace(bank.AccountNumber), "Tài khoản thực chi không còn hợp lệ trong cửa hàng.");
            Need(bank!.BankCode.Concat(bank.AccountNumber).All(c => c is >= ' ' and <= '~' || char.IsWhiteSpace(c)),
                "Mã ngân hàng hoặc số tài khoản thực chi có ký tự chưa hỗ trợ đối chiếu định danh; giữ hồ sơ để kiểm tra, không tự đổi thông tin tài khoản.");
            Need(Version(bank.RowVersion) == request.PayerBankAccountVersion,
                "Tài khoản thực chi đã thay đổi sau khi được chọn; kiểm tra lại bằng chứng và tải lại hồ sơ trước khi xác nhận.");
            if (plan is null || plan.BankId != bank!.Id || plan.AccountIdentity != BankPhysicalAccountIdentity.Create(bank.BankCode, bank.AccountNumber) || plan.AccountVersion != Version(bank.RowVersion))
                throw new StorefrontRetryableCommandException("Tài khoản thực chi vừa đổi cấu hình; thử lại đúng mã yêu cầu để khóa lại đủ bằng chứng.");
        }
        else Need(request.PayerBankAccountId is null, "Phiếu chi tiền mặt không được gắn tài khoản ngân hàng giả.");
        var accountIdentity = bank is null ? null : BankPhysicalAccountIdentity.Create(bank.BankCode, bank.AccountNumber);
        var method = source.CashId.HasValue ? "RECORDED_CASH_OUT" : source.Fund == "bank" ? "STAFF_VERIFIED_BANK_DEBIT" : "STAFF_VERIFIED_CASH_TREASURY";
        var key = accountIdentity is null ? source.Canonical : GlobalBankReceiptLedger.Hash("BANK-REFUND|" + accountIdentity + "|" + request.ExternalTransactionId);
        await StorefrontFulfillmentService.Lock(db, "GaoApp:Storefront:RefundPayout:" + GlobalBankReceiptLedger.Hash(key), ct);
        var snapshot = Serialize(new { treasuryEntryId = source.TreasuryId, cashTransactionId = source.CashId, fund = source.Fund, amount = source.Amount,
            sourceVersion = source.Version, cashVersion = source.CashVersion, treasuryVersion = source.TreasuryVersion,
            payerBankAccountId = bank?.Id, payerLegalEntityId = proof.Owner, payerAccountIdentity = accountIdentity,
            payerAccountVersion = bank is null ? null : Version(bank.RowVersion), externalTransactionId = request.ExternalTransactionId,
            executedAtUtc = at, verificationMethod = method, canonicalSourceIdentity = source.Canonical });
        var protectedContext = Serialize(new { actualSource = snapshot, manualAttestation = true, evidence = request.EvidenceReference.Trim(),
            payerAccount = bank is null ? null : new { bank.Id, bank.BankCode, bank.AccountNumber, bank.AccountName, version = Version(bank.RowVersion) } });
        return new(at, bank?.Id, method, key, snapshot, protectedContext);
    }
}
