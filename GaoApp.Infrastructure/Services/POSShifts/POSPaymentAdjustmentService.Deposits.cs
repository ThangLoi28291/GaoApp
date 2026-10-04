using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GaoApp.Infrastructure.Services.POSShifts;

public sealed partial class POSPaymentAdjustmentService
{
    private sealed record DepositLabel(string Number, int CustomerId, string CustomerName);

    private Task<Dictionary<int, DepositLabel>> DepositLabels(int[] ids, CancellationToken ct) =>
        (from e in db.Set<CustomerDepositEntry>().IgnoreQueryFilters().AsNoTracking()
         join d in db.Set<CustomerDeposit>().IgnoreQueryFilters() on e.CustomerDepositId equals d.Id
         join c in db.Customers.IgnoreQueryFilters() on d.CustomerId equals c.Id
         where e.StoreId == StoreId && d.StoreId == StoreId && c.StoreId == StoreId && ids.Contains(e.Id)
         select new { e.Id, DepositId = d.Id, c.Name, CustomerId = c.Id })
        .ToDictionaryAsync(x => x.Id, x => new DepositLabel($"DC-{x.DepositId}", x.CustomerId, x.Name), ct);

    private async Task<CustomerDepositEntry> DepositEntry(int id, bool tracked, CancellationToken ct)
    {
        var q = db.Set<CustomerDepositEntry>().IgnoreQueryFilters().Where(x => x.StoreId == StoreId && x.Id == id);
        return await (tracked ? q : q.AsNoTracking()).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundAppException("Không tìm thấy khoản nhận cọc.");
    }

    private async Task<string?> DepositUnavailable(CustomerDepositEntry e, CancellationToken ct)
    {
        if (e.IsDeleted || e.Kind != "Receive" || e.Amount <= 0 || e.Method is not (PaymentMethod.Cash or PaymentMethod.BankTransfer))
            return "Chỉ đổi phương thức của khoản nhận cọc tiền mặt hoặc chuyển khoản.";
        if (!await db.Set<CustomerDeposit>().AnyAsync(x => x.StoreId == StoreId && x.Id == e.CustomerDepositId, ct))
            return "Phiếu cọc không còn hợp lệ.";
        if (e.Method == PaymentMethod.BankTransfer && (!e.StoreBankAccountId.HasValue ||
            !await db.StoreBankAccounts.IgnoreQueryFilters().AnyAsync(x => x.StoreId == StoreId && x.Id == e.StoreBankAccountId && x.ConfirmMode == BankQrConfirmMode.Manual, ct)))
            return "Khoản cọc có thông tin ngân hàng chưa phù hợp để đổi phương thức. Cần quản lý kiểm tra.";
        return null;
    }

    // Legacy receipts used this exact note before an explicit entry FK was introduced.
    // Ambiguous evidence must be investigated, never guessed or merged.
    private async Task<POSShiftCashTransaction?> DepositCash(CustomerDepositEntry e, bool tracked, CancellationToken ct)
    {
        var note = $"Phiếu cọc DC-{e.CustomerDepositId}";
        var q = db.POSShiftCashTransactions.IgnoreQueryFilters().Where(x => x.StoreId == StoreId && x.POSShiftId == e.POSShiftId &&
            (x.CustomerDepositEntryId == e.Id || (x.CustomerDepositEntryId == null && x.Reason == "Nhận cọc khách hàng" && x.Note == note)));
        var rows = await (tracked ? q : q.AsNoTracking()).ToListAsync(ct);
        if (rows.Count > 1 || rows.Any(x => x.Amount != e.Amount || x.Type != POSShiftCashTransactionType.CashIn) ||
            (e.Method == PaymentMethod.Cash && (rows.Count != 1 || rows[0].IsDeleted)) ||
            (e.Method == PaymentMethod.BankTransfer && rows.Any(x => !x.IsDeleted)))
            throw new ConflictAppException("Phiếu thu cọc chưa khớp khoản nhận cọc. Cần quản lý kiểm tra trước khi đổi phương thức.");
        return rows.SingleOrDefault();
    }

    private async Task<int?> DepositBank(CustomerDepositEntry e, PaymentMethod method, CancellationToken ct)
    {
        if (method == PaymentMethod.Cash) return null;
        if (method != PaymentMethod.BankTransfer) throw new ValidationAppException("Cọc chỉ dùng tiền mặt hoặc chuyển khoản.");
        var bank = await db.StoreBankAccounts.AsNoTracking().Where(x => x.StoreId == StoreId && x.IsActive && x.ConfirmMode == BankQrConfirmMode.Manual &&
            (x.Id == e.StoreBankAccountId || x.IsDefault)).OrderByDescending(x => x.Id == e.StoreBankAccountId).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        return bank ?? throw new ConflictAppException("Cần cấu hình tài khoản ngân hàng thủ công để ghi nhận cọc chuyển khoản.");
    }

    private static decimal DepositDelta(CustomerDepositEntry e, PaymentMethod method) =>
        (method == PaymentMethod.Cash ? e.Amount : 0) - (e.Method == PaymentMethod.Cash ? e.Amount : 0);
    private static PaymentAdjustmentPosition DepositPosition(POSShift s, decimal delta = 0) => Position(s) with
    { Expected = s.ClosingCashExpected + delta, Difference = s.ClosingCashActual - s.ClosingCashExpected - delta };

    private static bool DepositMatches(POSPaymentAdjustmentRequest r, CustomerDepositEntry e, POSShiftCashTransaction? cash) =>
        Version(e.RowVersion) == r.PaymentVersion && e.Method == r.OldMethod && e.Amount == r.Amount &&
        e.Reference == r.OldReference && e.StoreBankAccountId == r.OldStoreBankAccountId && e.POSShiftId == r.POSShiftId &&
        cash?.Id == r.CashTransactionId && (cash == null || Version(cash.RowVersion) == r.CashTransactionVersion);

    public async Task<PagedResult<PaymentAdjustmentCandidateDto>> DepositsAsync(int page, string? keyword, int? entryId, int? shiftId, CancellationToken ct)
    {
        page = Math.Clamp(page, 1, 100000000);
        var q = from e in db.Set<CustomerDepositEntry>().AsNoTracking()
                join d in db.Set<CustomerDeposit>() on e.CustomerDepositId equals d.Id
                join c in db.Customers on d.CustomerId equals c.Id
                join s in db.POSShifts on e.POSShiftId equals s.Id
                where e.StoreId == StoreId && d.StoreId == StoreId && c.StoreId == StoreId && s.StoreId == StoreId &&
                    s.OpenedByUserId == UserId && e.Kind == "Receive"
                select new { Entry = e, DepositId = d.Id, CustomerId = c.Id, CustomerName = c.Name, Shift = s };
        if (entryId.HasValue) q = q.Where(x => x.Entry.Id == entryId);
        if (shiftId.HasValue) q = q.Where(x => x.Shift.Id == shiftId);
        if (Trim(keyword) is { } k) q = q.Where(x => x.CustomerName.Contains(k) || ("DC-" + x.DepositId).Contains(k) ||
            (x.Entry.Reference != null && x.Entry.Reference.Contains(k)));
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.Entry.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        var ids = rows.Select(x => x.Entry.Id).ToArray();
        var pending = await Requests.Where(x => x.DepositEntryId.HasValue && ids.Contains(x.DepositEntryId.Value) && x.Status == POSCashAdjustmentStatus.Pending)
            .ToDictionaryAsync(x => x.DepositEntryId!.Value, x => x.Id, ct);
        var result = new List<PaymentAdjustmentCandidateDto>();
        foreach (var x in rows)
        {
            var unavailable = await DepositUnavailable(x.Entry, ct);
            try { if (unavailable == null) _ = await DepositCash(x.Entry, false, ct); }
            catch (ConflictAppException ex) { unavailable = ex.Message; }
            var p = pending.GetValueOrDefault(x.Entry.Id);
            result.Add(new(x.Entry.Id, 0, $"DC-{x.DepositId}", x.Shift.Id, x.Shift.ShiftCode, x.Shift.Status.ToString(),
                x.Entry.Method?.ToString() ?? "", x.Entry.Amount, x.Entry.Reference, x.Entry.CreatedAtUtc, Version(x.Entry.RowVersion),
                unavailable == null && p == 0, unavailable, p == 0 ? null : p, x.Entry.Id, x.CustomerId, x.CustomerName));
        }
        return new(page, 20, total, result);
    }

    public async Task<PaymentAdjustmentPreviewDto> PreviewDepositAsync(int entryId, PaymentMethod method, CancellationToken ct)
    {
        var e = await DepositEntry(entryId, false, ct);
        var s = await Shift(e.POSShiftId, false, ct);
        if (s.OpenedByUserId != UserId) throw new ForbiddenAppException("Bạn chỉ được đề nghị đổi cọc trong ca của mình.");
        if (await DepositUnavailable(e, ct) is { } error) throw new ConflictAppException(error);
        _ = await DepositCash(e, false, ct);
        _ = await DepositBank(e, method, ct);
        var delta = DepositDelta(e, method);
        return new(DepositPosition(s), DepositPosition(s, delta), delta);
    }

    public async Task<int> CreateDepositAsync(int entryId, CreatePaymentAdjustmentRequest input, CancellationToken ct)
    {
        var reason = Reason(input.RequestReason);
        var reference = input.Method == PaymentMethod.Cash ? null : Trim(input.Reference);
        if (input.ClientRequestId == Guid.Empty || input.Method is not (PaymentMethod.Cash or PaymentMethod.BankTransfer) || reference?.Length > 100)
            throw new ValidationAppException("Kiểm tra mã yêu cầu và phương thức nhận cọc.");
        var seed = await DepositEntry(entryId, false, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var s = await Shift(seed.POSShiftId, true, ct);
        if (s.OpenedByUserId != UserId) throw new ForbiddenAppException("Bạn chỉ được đề nghị đổi cọc trong ca của mình.");
        var old = await Requests.SingleOrDefaultAsync(x => x.ClientRequestId == input.ClientRequestId, ct);
        if (old != null)
        {
            if (old.DepositEntryId == entryId && old.RequestedByUserId == UserId && old.NewMethod == input.Method && old.NewReference == reference &&
                old.RequestReason == reason && old.PaymentVersion == input.RowVersion) return old.Id;
            throw new ConflictAppException("Mã yêu cầu đã được sử dụng cho nội dung khác.");
        }
        var e = await DepositEntry(entryId, true, ct);
        if (e.POSShiftId != s.Id || await DepositUnavailable(e, ct) is { } error) throw new ConflictAppException("Khoản nhận cọc không còn phù hợp để điều chỉnh.");
        if (Version(e.RowVersion) != input.RowVersion) throw new ConflictAppException("Khoản nhận cọc đã thay đổi. Hãy tải lại.");
        if (await Requests.AnyAsync(x => x.DepositEntryId == entryId && x.Status == POSCashAdjustmentStatus.Pending, ct))
            throw new ConflictAppException("Khoản cọc đã có yêu cầu chờ duyệt.");
        if (e.Method == input.Method && e.Reference == reference) throw new ValidationAppException("Chưa có nội dung thay đổi.");
        var cash = await DepositCash(e, false, ct);
        if (cash != null && await db.Set<POSCashAdjustmentRequest>().AnyAsync(x => x.StoreId == StoreId && x.TransactionId == cash.Id && x.Status == POSCashAdjustmentStatus.Pending, ct))
            throw new ConflictAppException("Phiếu thu cọc đang có yêu cầu khác. Hãy xử lý yêu cầu đó trước.");
        var r = new POSPaymentAdjustmentRequest
        {
            StoreId = StoreId, ClientRequestId = input.ClientRequestId, DepositEntryId = e.Id, POSShiftId = s.Id,
            RequestedByUserId = UserId, RequestedByName = await Actor(ct), RequestReason = reason, PaymentVersion = Version(e.RowVersion),
            Amount = e.Amount, OldMethod = e.Method!.Value, NewMethod = input.Method, OldReference = e.Reference, NewReference = reference,
            OldStoreBankAccountId = e.StoreBankAccountId, NewStoreBankAccountId = await DepositBank(e, input.Method, ct),
            CashTransactionId = cash?.Id, CashTransactionVersion = cash == null ? null : Version(cash.RowVersion), ExpectedDelta = DepositDelta(e, input.Method)
        };
        db.Add(r); await Save(ct); await tx.CommitAsync(ct); return r.Id;
    }

    private async Task<PaymentAdjustmentDetailDto> DepositDetail(POSPaymentAdjustmentRequest r, bool isAdmin, CancellationToken ct)
    {
        var e = await DepositEntry(r.DepositEntryId!.Value, false, ct);
        var s = await Shift(r.POSShiftId, false, ct);
        var unavailable = r.Status == POSCashAdjustmentStatus.Pending ? await DepositUnavailable(e, ct) : null;
        if (r.Status == POSCashAdjustmentStatus.Pending && unavailable == null)
        {
            try
            {
                var cash = await DepositCash(e, false, ct);
                if (!DepositMatches(r, e, cash)) unavailable = "Khoản cọc hoặc phiếu thu đã thay đổi. Hãy rút hoặc từ chối để lập yêu cầu mới.";
                if (r.NewMethod == PaymentMethod.BankTransfer && !await db.StoreBankAccounts.AnyAsync(x => x.StoreId == StoreId && x.Id == r.NewStoreBankAccountId && x.IsActive && x.ConfirmMode == BankQrConfirmMode.Manual, ct))
                    unavailable = "Tài khoản ngân hàng đã thay đổi. Hãy kiểm tra và lập lại yêu cầu.";
            }
            catch (ConflictAppException ex) { unavailable = ex.Message; }
        }
        var label = (await DepositLabels([e.Id], ct)).GetValueOrDefault(e.Id);
        var delta = r.Status == POSCashAdjustmentStatus.Pending && unavailable == null ? DepositDelta(e, r.NewMethod) : 0;
        return new(Map(r, label?.Number ?? $"Cọc #{e.Id}", s.ShiftCode, label?.CustomerId, label?.CustomerName), DepositPosition(s), DepositPosition(s, delta),
            Version(s.RowVersion), isAdmin && r.Status == POSCashAdjustmentStatus.Pending && unavailable == null,
            r.RequestedByUserId == UserId && r.Status == POSCashAdjustmentStatus.Pending, unavailable);
    }

    private async Task DecideDeposit(POSPaymentAdjustmentRequest seed, string action, PaymentAdjustmentDecision input, string? note, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Only receipt classification changes: no deposit balance/order locks or ledger mutations are needed.
        var s = await Shift(seed.POSShiftId, true, ct);
        var r = await Requests.SingleAsync(x => x.Id == seed.Id, ct);
        var next = action == "approve" ? POSCashAdjustmentStatus.Approved : action == "reject" ? POSCashAdjustmentStatus.Rejected : POSCashAdjustmentStatus.Withdrawn;
        if (r.Status == next) return;
        if (r.Status != POSCashAdjustmentStatus.Pending || Version(r.RowVersion) != input.RowVersion)
            throw new ConflictAppException("Yêu cầu đã được xử lý hoặc thay đổi. Hãy tải lại.");
        if (action == "approve")
        {
            var e = await DepositEntry(r.DepositEntryId!.Value, true, ct);
            if (await DepositUnavailable(e, ct) is { } error) throw new ConflictAppException(error);
            var cash = await DepositCash(e, true, ct);
            if (!DepositMatches(r, e, cash)) throw new ConflictAppException("Khoản cọc hoặc phiếu thu đã thay đổi. Hãy từ chối và lập lại yêu cầu.");
            if (cash != null && await db.Set<POSCashAdjustmentRequest>().AnyAsync(x => x.StoreId == StoreId && x.TransactionId == cash.Id && x.Status == POSCashAdjustmentStatus.Pending, ct))
                throw new ConflictAppException("Phiếu thu cọc có yêu cầu khác đang chờ xử lý.");
            if (Version(s.RowVersion) != input.ShiftRowVersion) throw new ConflictAppException("Số liệu ca đã thay đổi. Mở lại yêu cầu trước khi duyệt.");
            if (r.NewMethod == PaymentMethod.BankTransfer && !await db.StoreBankAccounts.AnyAsync(x => x.StoreId == StoreId && x.Id == r.NewStoreBankAccountId && x.IsActive && x.ConfirmMode == BankQrConfirmMode.Manual, ct))
                throw new ConflictAppException("Tài khoản ngân hàng đã thay đổi. Hãy kiểm tra và lập lại yêu cầu.");
            var delta = DepositDelta(e, r.NewMethod);
            if (s.CashInTotal + delta < 0 || s.ClosingCashExpected != s.OpeningCash + s.CashSalesTotal + s.CashInTotal - s.CashOutTotal - s.CashRefundTotal)
                throw new ConflictAppException("Tổng quỹ chưa khớp phiếu. Cần kiểm tra trước khi duyệt.");
            r.BeforeShiftJson = JsonSerializer.Serialize(DepositPosition(s), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (cash != null) cash.CustomerDepositEntryId = e.Id;
            if (r.NewMethod == PaymentMethod.Cash && e.Method != PaymentMethod.Cash)
            {
                if (cash == null)
                {
                    cash = new POSShiftCashTransaction { StoreId = StoreId, POSShiftId = s.Id, CustomerDepositEntryId = e.Id,
                        Amount = e.Amount, Type = POSShiftCashTransactionType.CashIn, Reason = "Nhận cọc khách hàng", Note = $"Phiếu cọc DC-{e.CustomerDepositId}",
                        CreatedByUserId = s.OpenedByUserId, CreatedAtUtc = e.CreatedAtUtc };
                    db.Add(cash);
                }
                else { cash.IsDeleted = false; cash.DeletedAtUtc = null; cash.DeletedBy = null; }
            }
            else if (r.NewMethod != PaymentMethod.Cash && e.Method == PaymentMethod.Cash)
            { cash!.IsDeleted = true; cash.DeletedAtUtc = DateTime.UtcNow; cash.DeletedBy = UserId; }
            e.Method = r.NewMethod; e.Reference = r.NewReference; e.StoreBankAccountId = r.NewStoreBankAccountId;
            s.CashInTotal += delta; s.RecalcExpected(); s.UpdatedAtUtc = DateTime.UtcNow; s.UpdatedBy = UserId;
            r.ExpectedDelta = delta; r.AppliedToClosedShift = s.Status == POSShiftStatus.Closed;
            if (r.AppliedToClosedShift) s.NeedsCashReconciliation = true;
            r.AfterShiftJson = JsonSerializer.Serialize(DepositPosition(s), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        r.Status = next; r.ReviewedByUserId = UserId; r.ReviewedByName = await Actor(ct); r.ReviewedAtUtc = DateTime.UtcNow; r.ReviewNote = note;
        await Save(ct); await tx.CommitAsync(ct);
    }
}
