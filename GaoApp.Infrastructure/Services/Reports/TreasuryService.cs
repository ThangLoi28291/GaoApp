using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.Interfaces.Services.Reports;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Reports;

public sealed class TreasuryService(AppDbContext db) : ITreasuryService
{
    private int StoreId => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new ForbiddenAppException("Chưa chọn cửa hàng.");
    private static readonly ReportOptionDto[] Funds = [new("cash", "Tiền mặt"), new("bank", "Chuyển khoản")];
    public async Task<TreasurySourcesDto> SourcesAsync(CancellationToken ct, int? expenseId = null, int? payableId = null)
    {
        if (expenseId is <= 0 || payableId is <= 0) throw new ArgumentException("Mã chứng từ không hợp lệ.");
        var expenses = await db.OperatingExpenses.AsNoTracking().Where(x => x.StoreId == StoreId && x.Status == "confirmed" && (expenseId == null || x.Id == expenseId)
            && (!x.IsPaid || !db.TreasuryEntries.Any(y => y.StoreId == StoreId && y.OperatingExpenseId == x.Id)))
            .OrderByDescending(x => x.Id).Take(500).ToListAsync(ct);
        var payables = await db.Set<PurchasePayable>().AsNoTracking().Where(x => x.StoreId == StoreId &&
            (x.Status == PurchasePayableStatus.Outstanding || x.Status == PurchasePayableStatus.Paid && !db.TreasuryEntries.Any(y => y.StoreId == StoreId && y.PurchasePayableId == x.Id)) && (payableId == null || x.Id == payableId))
            .OrderByDescending(x => x.Id).Take(500).ToListAsync(ct);
        var settled = await db.TreasuryEntries.AsNoTracking().Where(x => x.StoreId == StoreId && x.PurchasePayableId != null)
            .GroupBy(x => x.PurchasePayableId!.Value).Select(x => new { Id = x.Key, Amount = -x.Sum(y => y.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
        var linked = db.TreasuryEntries.Where(x => x.StoreId == StoreId && x.POSShiftCashTransactionId != null).Select(x => x.POSShiftCashTransactionId);
        var cash = await db.POSShiftCashTransactions.AsNoTracking().Where(x => x.StoreId == StoreId && x.CustomerDepositEntryId == null && x.CustomerDebtReceiptId == null
            && x.Reason != "Thu công nợ khách hàng" && x.Reason != "Nhận cọc khách hàng" && x.Reason != "Hoàn cọc khách hàng" && !linked.Contains(x.Id))
            .OrderByDescending(x => x.Id).Take(200).ToListAsync(ct);
        return new(Funds, expenses.Select(x => new TreasurySourceDto(x.Id, $"CP-{x.Id} · {x.Name}" + (x.IsPaid ? " · Bổ sung chứng từ thanh toán" : ""), x.Amount, Version(x.RowVersion), RequiresEvidence: x.IsPaid)).ToList(),
            payables.Select(x => new TreasurySourceDto(x.Id, $"NCC-{x.Id} · {x.PayeeName ?? "Nhà cung cấp"}" + (x.Status == PurchasePayableStatus.Paid ? " · Bổ sung chứng từ thanh toán" : ""),
                x.Amount - settled.GetValueOrDefault(x.Id), Version(x.RowVersion), x.Status == PurchasePayableStatus.Paid ? x.PaidAtUtc?.AddHours(7).Date : null, x.Status == PurchasePayableStatus.Paid)).Where(x => x.Amount > 0).ToList(),
            cash.Select(x => new TreasurySourceDto(x.Id, $"CA-{x.POSShiftId} · {x.Reason}", x.Type == POSShiftCashTransactionType.CashIn ? x.Amount : -x.Amount, Version(x.RowVersion), x.CreatedAtUtc.AddHours(7).Date)).ToList());
    }
    public async Task<int> CreateAsync(TreasuryWriteDto r, CancellationToken ct)
    {
        ValidateDate(r.Date); ValidateAmount(r.Amount); ValidateFund(r.Fund);
        if (r.ClientRequestId == Guid.Empty || string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 200 || r.Reference?.Length > 200 || r.Note?.Length > 1000)
            throw new ArgumentException("Kiểm tra tên khoản thu chi, mã yêu cầu và độ dài ghi chú.");
        if (r.OperatingExpenseId != null && r.PurchasePayableId != null) throw new ArgumentException("Chỉ chọn một chứng từ cần thanh toán.");
        if (r.TargetFund != null) {
            ValidateFund(r.TargetFund);
            var internalCash = r.TargetFund == "cash" && r.Fund == "cash" && r.POSShiftCashTransactionId != null;
            if ((!internalCash && (r.TargetFund == r.Fund || r.Amount >= 0)) || r.OperatingExpenseId != null || r.PurchasePayableId != null)
                throw new ArgumentException("Chuyển quỹ cần hai quỹ khác nhau, số tiền chi và không kèm chứng từ thanh toán.");
        }
        var payload = JsonSerializer.Serialize(new { r.Fund, r.TargetFund, r.Amount, Date = r.Date.Date, Name = r.Name.Trim(), Reference = r.Reference?.Trim(),
            Note = r.Note?.Trim(), r.OperatingExpenseId, r.PurchasePayableId, r.POSShiftCashTransactionId, r.ReconcileLegacyPayment });
        return await LockedAsync(async () => {
            var previous = await ExistingAsync(r.ClientRequestId, payload, ct);
            if (previous != null) return previous.Id;
            var at = r.Date.Date.AddHours(5); // Local noon, persisted in UTC; date-only input is explicit in the UI.
            if (r.POSShiftCashTransactionId is int cashId) {
                var cash = await db.POSShiftCashTransactions.FromSqlInterpolated($"SELECT * FROM [POSShiftCashTransactions] WITH (UPDLOCK,HOLDLOCK) WHERE [StoreId]={StoreId} AND [Id]={cashId}").SingleOrDefaultAsync(ct)
                    ?? throw new KeyNotFoundException("Không tìm thấy phiếu tiền mặt.");
                if (cash.CustomerDebtReceiptId != null || cash.CustomerDepositEntryId != null || cash.Reason is "Thu công nợ khách hàng" or "Nhận cọc khách hàng" or "Hoàn cọc khách hàng")
                    throw new ConflictAppException("Phiếu này đã được đối chiếu qua công nợ hoặc tiền cọc.");
                var signed = cash.Type == POSShiftCashTransactionType.CashIn ? cash.Amount : -cash.Amount;
                if (r.Fund != "cash" || r.Amount != signed || cash.CreatedAtUtc.AddHours(7).Date != r.Date.Date)
                    throw new ArgumentException("Liên kết phiếu ca phải khớp quỹ tiền mặt, số tiền và ngày phiếu gốc.");
                if (await db.TreasuryEntries.AnyAsync(x => x.StoreId == StoreId && x.POSShiftCashTransactionId == cashId, ct))
                    throw new ConflictAppException("Phiếu tiền mặt đã được liên kết.");
                if (await db.Set<POSCashAdjustmentRequest>().AnyAsync(x => x.StoreId == StoreId && x.TransactionId == cashId && x.Status == POSCashAdjustmentStatus.Pending, ct))
                    throw new ConflictAppException("Phiếu tiền mặt đang chờ duyệt điều chỉnh. Xử lý yêu cầu trước khi liên kết.");
                at = cash.CreatedAtUtc;
            }
            if (r.OperatingExpenseId is int expenseId) {
                var expense = await db.OperatingExpenses.SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == expenseId, ct)
                    ?? throw new KeyNotFoundException("Không tìm thấy chi phí.");
                CheckVersion(expense.RowVersion, r.SourceRowVersion);
                var legacy = expense.IsPaid && r.ReconcileLegacyPayment && !await db.TreasuryEntries.AnyAsync(x => x.StoreId == StoreId && x.OperatingExpenseId == expenseId, ct);
                if (expense.Status != "confirmed" || expense.IsPaid && !legacy || r.Amount != -expense.Amount)
                    throw new ConflictAppException("Chi phí phải đã ghi nhận, chưa thanh toán và khớp toàn bộ số tiền.");
                expense.IsPaid = true; expense.PaymentMethod = r.Fund; expense.ReceiptReference = r.Reference?.Trim();
            }
            if (r.PurchasePayableId is int payableId) {
                var payable = await db.Set<PurchasePayable>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == payableId, ct)
                    ?? throw new KeyNotFoundException("Không tìm thấy khoản phải trả.");
                CheckVersion(payable.RowVersion, r.SourceRowVersion);
                var paid = -await db.TreasuryEntries.Where(x => x.StoreId == StoreId && x.PurchasePayableId == payableId).SumAsync(x => x.Amount, ct);
                var legacy = payable.Status == PurchasePayableStatus.Paid && r.ReconcileLegacyPayment &&
                    !await db.TreasuryEntries.AnyAsync(x => x.StoreId == StoreId && x.PurchasePayableId == payableId, ct);
                if ((payable.Status != PurchasePayableStatus.Outstanding && !legacy) || legacy && -r.Amount != payable.Amount || r.Amount >= 0 || -r.Amount > payable.Amount - paid || r.Date.Date < payable.RecognizedAtUtc.AddHours(7).Date)
                    throw new ConflictAppException("Số tiền vượt dư nợ, khoản đã đóng hoặc ngày thanh toán trước ngày ghi nhận.");
                if (at < payable.RecognizedAtUtc) at = payable.RecognizedAtUtc;
                if (paid - r.Amount == payable.Amount) { payable.Status = PurchasePayableStatus.Paid; payable.PaidAtUtc = at; }
                payable.UpdatedAtUtc = DateTime.UtcNow;
            }
            var entry = new TreasuryEntry { StoreId = StoreId, ClientRequestId = r.ClientRequestId, RequestJson = payload, Fund = r.Fund,
                TargetFund = r.TargetFund, Amount = r.Amount, OccurredAtUtc = at, Name = r.Name.Trim(), Reference = r.Reference?.Trim(), Note = r.Note?.Trim(),
                OperatingExpenseId = r.OperatingExpenseId, PurchasePayableId = r.PurchasePayableId, POSShiftCashTransactionId = r.POSShiftCashTransactionId, IsVoucherLink = r.POSShiftCashTransactionId != null };
            db.TreasuryEntries.Add(entry); await db.SaveChangesAsync(ct); return entry.Id;
        }, ct);
    }
    public async Task<int> ReverseAsync(int id, TreasuryReverseDto r, CancellationToken ct)
    {
        ValidateDate(r.Date);
        if (r.ClientRequestId == Guid.Empty || string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Trim().Length > 500) throw new ArgumentException("Nhập lý do đảo phiếu.");
        var payload = JsonSerializer.Serialize(new { Reverse = id, Date = r.Date.Date, Reason = r.Reason.Trim() });
        return await LockedAsync(async () => {
            var previous = await ExistingAsync(r.ClientRequestId, payload, ct); if (previous != null) return previous.Id;
            var original = await db.TreasuryEntries.SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy phiếu.");
            CheckVersion(original.RowVersion, r.RowVersion);
            if (original.ReversedAtUtc != null || original.ReversalOfId != null)
                throw new ConflictAppException("Phiếu đã đảo hoặc không phải phiếu gốc.");
            var at = r.Date.Date.AddHours(5);
            if (r.Date.Date < original.OccurredAtUtc.AddHours(7).Date) throw new ArgumentException("Ngày đảo phải từ ngày phiếu gốc trở đi.");
            if (at < original.OccurredAtUtc) at = original.OccurredAtUtc;
            var entry = new TreasuryEntry { StoreId = StoreId, ClientRequestId = r.ClientRequestId, RequestJson = payload,
                Fund = original.Fund, TargetFund = original.TargetFund, Amount = -original.Amount, OccurredAtUtc = at,
                Name = "Đảo phiếu: " + original.Name[..Math.Min(original.Name.Length, 180)], Note = r.Reason.Trim(), ReversalOfId = id,
                OperatingExpenseId = original.OperatingExpenseId, PurchasePayableId = original.PurchasePayableId, IsVoucherLink = original.IsVoucherLink };
            original.ReversedAtUtc = at;
            // Cancelling a classification reverses debt settlement, never moves drawer cash again.
            if (original.IsVoucherLink) original.POSShiftCashTransactionId = null;
            if (original.OperatingExpenseId is int expenseId) {
                var expense = await db.OperatingExpenses.SingleAsync(x => x.StoreId == StoreId && x.Id == expenseId, ct); expense.IsPaid = false;
            }
            if (original.PurchasePayableId is int payableId) {
                var payable = await db.Set<PurchasePayable>().SingleAsync(x => x.StoreId == StoreId && x.Id == payableId, ct);
                payable.Status = PurchasePayableStatus.Outstanding; payable.PaidAtUtc = null;
            }
            db.TreasuryEntries.Add(entry); await db.SaveChangesAsync(ct); return entry.Id;
        }, ct);
    }
    public async Task OpeningAsync(TreasuryOpeningDto r, CancellationToken ct)
    {
        ValidateDate(r.Date); ValidateFund(r.Fund);
        if (Math.Abs(r.Amount) > 1_000_000_000_000m || decimal.Round(r.Amount, 2) != r.Amount || string.IsNullOrWhiteSpace(r.Note) || r.Note.Trim().Length > 1000)
            throw new ArgumentException("Kiểm tra số dư và ghi rõ căn cứ đối chiếu.");
        await LockedAsync(async () => {
            var existing = await db.TreasuryOpeningBalances.SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Fund == r.Fund, ct);
            if (existing != null) {
                if (existing.AsOfDate == r.Date.Date && existing.Amount == r.Amount && existing.Note == r.Note.Trim()) return existing.Id;
                throw new ConflictAppException("Quỹ đã có số dư ban đầu. Điều chỉnh bằng phiếu thu chi có căn cứ để giữ lịch sử.");
            }
            var x = new TreasuryOpeningBalance { StoreId = StoreId, Fund = r.Fund, AsOfDate = r.Date.Date, Amount = r.Amount, Note = r.Note.Trim() };
            db.TreasuryOpeningBalances.Add(x); await db.SaveChangesAsync(ct); return x.Id;
        }, ct);
    }
    public async Task SetDueDateAsync(int id, PayableDueDateDto r, CancellationToken ct)
    {
        if (r.DueDate.HasValue) ValidateDate(r.DueDate.Value, false);
        await LockedAsync(async () => {
            var x = await db.Set<PurchasePayable>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy khoản phải trả.");
            CheckVersion(x.RowVersion, r.RowVersion); x.DueDate = r.DueDate?.Date; await db.SaveChangesAsync(ct); return x.Id;
        }, ct);
    }
    private async Task<T> LockedAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var resource = $"treasury:{StoreId}";
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=5000; IF @r<0 THROW 51000,'Treasury is busy.',1;", ct);
            try { var result = await action(); await tx.CommitAsync(ct); return result; }
            catch (DbUpdateConcurrencyException) { throw new ConflictAppException("Chứng từ đã thay đổi. Tải lại trước khi tiếp tục."); }
        });
    }
    private async Task<TreasuryEntry?> ExistingAsync(Guid requestId, string payload, CancellationToken ct)
    {
        var x = await db.TreasuryEntries.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.ClientRequestId == requestId, ct);
        if (x != null && (x.IsDeleted || x.RequestJson != payload)) throw new ConflictAppException("Mã yêu cầu đã dùng cho một phiếu khác.");
        return x;
    }
    private static string Version(byte[] bytes) => Convert.ToBase64String(bytes);
    private static void CheckVersion(byte[] bytes, string? expected) {
        if (Version(bytes) != expected) throw new ConflictAppException("Chứng từ đã thay đổi. Tải lại để tiếp tục.");
    }
    private static void ValidateFund(string fund) { if (fund is not ("cash" or "bank")) throw new ArgumentException("Chọn quỹ tiền mặt hoặc chuyển khoản."); }
    private static void ValidateAmount(decimal amount) {
        if (amount == 0 || Math.Abs(amount) > 1_000_000_000_000m || decimal.Round(amount, 2) != amount) throw new ArgumentException("Số tiền phải khác 0, tối đa 1.000 tỷ và tối đa 2 số thập phân.");
    }
    private static void ValidateDate(DateTime date, bool noFuture = true) {
        if (date.Year < 2000 || date.Year > 2100 || noFuture && date.Date > DateTime.UtcNow.AddHours(7).Date) throw new ArgumentException("Ngày không hợp lệ; thu chi thực tế không dùng ngày tương lai.");
    }
}
