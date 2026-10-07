using System.Text.Json;
using GaoApp.Application.Common;
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

public sealed class POSCashAdjustmentService(AppDbContext db, ITenantContext tenant, ICurrentUser user, IStoreAdminAccess admin)
    : IPOSCashAdjustmentService
{
    private int StoreId => tenant.StoreId is > 0 ? tenant.StoreId.Value : throw new ForbiddenAppException("Chưa chọn cửa hàng.");
    private int UserId => user.UserId is > 0 ? user.UserId.Value : throw new ForbiddenAppException("Vui lòng đăng nhập lại.");
    private IQueryable<POSCashAdjustmentRequest> Requests => db.Set<POSCashAdjustmentRequest>().Where(x => x.StoreId == StoreId && !x.IsDeleted);
    private static string Version(byte[] value) => Convert.ToBase64String(value);
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string RequiredNote(string? value) => Trim(value) is { Length: <= 500 } text ? text : throw new ValidationAppException("Nhập lý do từ 1 đến 500 ký tự.");
    private static decimal Signed(POSShiftCashTransactionType type, decimal amount) => type == POSShiftCashTransactionType.CashIn ? amount : -amount;
    private static decimal Delta(POSCashAdjustmentRequest r) => (r.IsCancellation ? 0 : Signed(r.NewType, r.NewAmount)) - Signed(r.OldType, r.OldAmount);
    private static ShiftCashPosition Position(POSShift s, decimal inDelta = 0, decimal outDelta = 0)
    {
        var expected = s.ClosingCashExpected + inDelta - outDelta;
        return new(s.CashInTotal + inDelta, s.CashOutTotal + outDelta, expected, s.ClosingCashActual,
            s.CashReceivedAmount, s.ClosingCashActual - expected, s.CashReceivedAmount - expected, s.NeedsCashReconciliation);
    }
    private static (decimal In, decimal Out) Deltas(POSCashAdjustmentRequest r) => (
        (r.IsCancellation || r.NewType != POSShiftCashTransactionType.CashIn ? 0 : r.NewAmount) - (r.OldType == POSShiftCashTransactionType.CashIn ? r.OldAmount : 0),
        (r.IsCancellation || r.NewType != POSShiftCashTransactionType.CashOut ? 0 : r.NewAmount) - (r.OldType == POSShiftCashTransactionType.CashOut ? r.OldAmount : 0));

    private async Task<string> ActorName(CancellationToken ct) => await db.Users.Where(x => x.Id == UserId)
        .Select(x => x.FullName ?? x.UserName).SingleAsync(ct);
    private async Task<POSShift> Shift(int id, bool locked, CancellationToken ct)
    {
        var q = locked ? db.POSShifts.FromSqlInterpolated($"SELECT * FROM [POSShifts] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id} AND [StoreId]={StoreId}")
            : db.POSShifts.AsNoTracking().Where(s => s.Id == id && s.StoreId == StoreId);
        return await q.SingleOrDefaultAsync(s => !s.IsDeleted, ct) ?? throw new NotFoundAppException("Không tìm thấy ca POS.");
    }
    private async Task<POSCashAdjustmentRequest> Request(int id, CancellationToken ct) =>
        await Requests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundAppException("Không tìm thấy yêu cầu.");

    public Task<Dictionary<int, int>> PendingAsync(int[] transactionIds, CancellationToken ct) => Requests
        .Where(x => x.RequestedByUserId == UserId && transactionIds.Contains(x.TransactionId) && x.Status == POSCashAdjustmentStatus.Pending)
        .ToDictionaryAsync(x => x.TransactionId, x => x.Id, ct);

    public async Task<PagedResult<CashAdjustmentTransactionDto>> TransactionsAsync(int page, string? keyword, int? transactionId, int? shiftId, CancellationToken ct)
    {
        page = Math.Clamp(page, 1, 100000000);
        // Employees only see their own manual cash vouchers, including cancelled ones for evidence.
        var q = db.Set<POSShiftCashTransaction>().IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == StoreId && x.CreatedByUserId == UserId &&
            x.CustomerDepositEntryId == null && !((x.Reason == "Nhận cọc khách hàng" || x.Reason == "Hoàn cọc khách hàng") && x.Note != null && x.Note.StartsWith("Phiếu cọc DC-")));
        if (transactionId.HasValue) q = q.Where(x => x.Id == transactionId);
        if (shiftId.HasValue) q = q.Where(x => x.POSShiftId == shiftId);
        if (!string.IsNullOrWhiteSpace(keyword)) { var k = keyword.Trim(); q = q.Where(x => x.Reason.Contains(k) || (x.Note != null && x.Note.Contains(k))); }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var pending = await Requests.Where(x => ids.Contains(x.TransactionId) && x.Status == POSCashAdjustmentStatus.Pending).ToDictionaryAsync(x => x.TransactionId, x => x.Id, ct);
        var shifts = await ShiftLabels(rows.Select(x => x.POSShiftId).ToArray(), ct);
        return new(page, 20, total, rows.Select(x => { shifts.TryGetValue(x.POSShiftId, out var s); return new CashAdjustmentTransactionDto(
            x.Id, x.POSShiftId, s?.Code, s?.Terminal, s?.Status ?? "", x.IsDeleted, new(x.Type.ToString(), x.Amount, x.Reason, x.Note),
            x.CreatedAtUtc, Version(x.RowVersion), pending.TryGetValue(x.Id, out var p) ? p : null); }).ToList());
    }
    private sealed record Label(string? Code, string? Terminal, string Status);
    private async Task<Dictionary<int, Label>> ShiftLabels(int[] ids, CancellationToken ct) => await db.POSShifts.AsNoTracking()
        .Where(s => s.StoreId == StoreId && ids.Contains(s.Id))
        .Select(s => new { s.Id, s.ShiftCode, s.Status, Terminal = db.POSTerminals.Where(t => t.StoreId == StoreId && t.Id == s.TerminalId).Select(t => t.Name).FirstOrDefault() })
        .ToDictionaryAsync(s => s.Id, s => new Label(s.ShiftCode, s.Terminal, s.Status.ToString()), ct);
    private static CashAdjustmentItemDto Map(POSCashAdjustmentRequest x, Label? s) => new(x.Id, x.TransactionId, x.POSShiftId, s?.Code, s?.Terminal,
        x.RequestedByName, x.CreatedAtUtc, x.Status.ToString(), x.IsCancellation, x.RequestReason,
        new(x.OldType.ToString(), x.OldAmount, x.OldReason, x.OldNote), new(x.NewType.ToString(), x.NewAmount, x.NewReason, x.NewNote),
        Delta(x), Version(x.RowVersion), x.ReviewedByName, x.ReviewedAtUtc, x.ReviewNote, x.BeforeShiftJson, x.AfterShiftJson,
        x.AppliedToClosedShift, x.ReconciledAtUtc, x.ReconciledByName, x.ReconciliationNote);

    public async Task<PagedResult<CashAdjustmentItemDto>> ListAsync(int page, string? status, int? shiftId, CancellationToken ct)
    {
        page = Math.Clamp(page, 1, 100000000); var q = Requests.AsNoTracking();
        if (!await admin.IsAdminAsync(ct)) q = q.Where(x => x.RequestedByUserId == UserId);
        if (shiftId.HasValue) q = q.Where(x => x.POSShiftId == shiftId);
        if (status == "NeedsReconciliation") q = q.Where(x => x.Status == POSCashAdjustmentStatus.Approved && x.AppliedToClosedShift && x.ReconciledAtUtc == null);
        else if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<POSCashAdjustmentStatus>(status, out var value) || !Enum.IsDefined(value)) throw new ValidationAppException("Trạng thái không hợp lệ.");
            q = q.Where(x => x.Status == value);
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        var shifts = await ShiftLabels(rows.Select(x => x.POSShiftId).ToArray(), ct);
        return new(page, 20, total, rows.Select(x => Map(x, shifts.GetValueOrDefault(x.POSShiftId))).ToList());
    }

    public async Task<CashAdjustmentDetailDto> DetailAsync(int id, CancellationToken ct)
    {
        var r = await Request(id, ct); var isAdmin = await admin.IsAdminAsync(ct);
        if (!isAdmin && r.RequestedByUserId != UserId) throw new ForbiddenAppException("Bạn chỉ được xem yêu cầu của mình.");
        var s = await Shift(r.POSShiftId, false, ct);
        var labels = await ShiftLabels([s.Id], ct); var d = Deltas(r);
        var valid = await db.Set<POSShiftCashTransaction>().AnyAsync(x => x.StoreId == StoreId && x.Id == r.TransactionId && !x.IsDeleted && x.RowVersion == Convert.FromBase64String(r.TransactionVersion) &&
            x.CustomerDepositEntryId == null && !((x.Reason == "Nhận cọc khách hàng" || x.Reason == "Hoàn cọc khách hàng") && x.Note != null && x.Note.StartsWith("Phiếu cọc DC-")), ct);
        return new(Map(r, labels.GetValueOrDefault(s.Id)), Position(s), r.Status == POSCashAdjustmentStatus.Pending ? Position(s, d.In, d.Out) : Position(s),
            Version(s.RowVersion), isAdmin && valid && r.Status == POSCashAdjustmentStatus.Pending,
            r.RequestedByUserId == UserId && r.Status == POSCashAdjustmentStatus.Pending);
    }

    public async Task<int> CreateAsync(int transactionId, CreateCashAdjustmentRequest input, CancellationToken ct)
    {
        var reason = RequiredNote(input.RequestReason);
        if (input.ClientRequestId == Guid.Empty) throw new ValidationAppException("Thiếu mã yêu cầu. Vui lòng mở lại biểu mẫu.");
        if (!input.IsCancellation && (!Enum.IsDefined(input.Type) || input.Amount <= 0 || input.Amount > 9999999999999.99m || decimal.Round(input.Amount, 2) != input.Amount
            || Trim(input.Reason) is not { Length: <= 300 } || Trim(input.Note)?.Length > 500)) throw new ValidationAppException("Kiểm tra loại thu/chi, số tiền và nội dung đề nghị.");
        var seed = await db.Set<POSShiftCashTransaction>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == transactionId, ct)
            ?? throw new NotFoundAppException("Không tìm thấy phiếu thu/chi.");
        if (seed.CreatedByUserId != UserId) throw new ForbiddenAppException("Bạn chỉ được đề nghị sửa/hủy phiếu do mình tạo.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Shift(seed.POSShiftId, true, ct);
        var existing = await Requests.SingleOrDefaultAsync(x => x.ClientRequestId == input.ClientRequestId, ct);
        if (existing != null)
        {
            if (existing.RequestedByUserId == UserId && existing.TransactionId == transactionId && existing.IsCancellation == input.IsCancellation && existing.RequestReason == reason &&
                (input.IsCancellation || (existing.NewType == input.Type && existing.NewAmount == input.Amount && existing.NewReason == Trim(input.Reason) && existing.NewNote == Trim(input.Note)))) return existing.Id;
            throw new ConflictAppException("Mã yêu cầu đã được sử dụng cho nội dung khác.");
        }
        var row = await db.Set<POSShiftCashTransaction>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == transactionId && !x.IsDeleted, ct)
            ?? throw new ConflictAppException("Phiếu đã bị hủy.");
        if (DepositVoucher(row)) throw new ConflictAppException("Đây là phiếu cọc. Hãy đề nghị đổi phương thức từ khoản nhận cọc để số quỹ và sổ cọc cùng khớp.");
        if (await db.TreasuryEntries.AnyAsync(x => x.StoreId == StoreId && x.POSShiftCashTransactionId == row.Id, ct))
            throw new ConflictAppException("Phiếu đã liên kết sổ thu chi. Hủy liên kết trong báo cáo dòng tiền trước khi sửa/hủy phiếu ca.");
        if (Version(row.RowVersion) != input.RowVersion) throw new ConflictAppException("Phiếu đã thay đổi. Vui lòng tải lại.");
        if (await Requests.AnyAsync(x => x.TransactionId == transactionId && x.Status == POSCashAdjustmentStatus.Pending, ct)) throw new ConflictAppException("Phiếu đã có yêu cầu đang chờ duyệt.");
        if (!input.IsCancellation && row.Type == input.Type && row.Amount == input.Amount && row.Reason == Trim(input.Reason) && row.Note == Trim(input.Note)) throw new ValidationAppException("Chưa có nội dung thay đổi.");
        var r = new POSCashAdjustmentRequest { StoreId = StoreId, ClientRequestId = input.ClientRequestId, TransactionId = row.Id, POSShiftId = row.POSShiftId,
            RequestedByUserId = UserId, RequestedByName = await ActorName(ct), RequestReason = reason, IsCancellation = input.IsCancellation,
            TransactionVersion = Version(row.RowVersion), OldType = row.Type, OldAmount = row.Amount, OldReason = row.Reason, OldNote = row.Note,
            NewType = input.IsCancellation ? row.Type : input.Type, NewAmount = input.IsCancellation ? 0 : input.Amount,
            NewReason = input.IsCancellation ? row.Reason : Trim(input.Reason)!, NewNote = input.IsCancellation ? row.Note : Trim(input.Note) };
        db.Add(r); await Save(ct); await tx.CommitAsync(ct); return r.Id;
    }

    public async Task DecideAsync(int id, string action, CashAdjustmentDecision input, CancellationToken ct)
    {
        if (action is not ("approve" or "reject" or "withdraw")) throw new ValidationAppException("Thao tác không hợp lệ.");
        if (action != "withdraw") await admin.RequireAsync(ct);
        var seed = await Request(id, ct);
        if (action == "withdraw" && seed.RequestedByUserId != UserId) throw new ForbiddenAppException("Bạn chỉ được rút yêu cầu của mình.");
        var note = action == "reject" ? RequiredNote(input.Note) : Trim(input.Note);
        if (note?.Length > 500) throw new ValidationAppException("Ghi chú tối đa 500 ký tự.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var shift = await Shift(seed.POSShiftId, true, ct);
        var r = await Requests.SingleAsync(x => x.Id == id, ct);
        var next = action == "approve" ? POSCashAdjustmentStatus.Approved : action == "reject" ? POSCashAdjustmentStatus.Rejected : POSCashAdjustmentStatus.Withdrawn;
        if (r.Status == next) return; // Network retry never applies the delta twice.
        if (r.Status != POSCashAdjustmentStatus.Pending || Version(r.RowVersion) != input.RowVersion) throw new ConflictAppException("Yêu cầu đã được xử lý hoặc thay đổi. Vui lòng tải lại.");
        if (action == "approve")
        {
            var row = await db.Set<POSShiftCashTransaction>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == r.TransactionId && x.POSShiftId == shift.Id && !x.IsDeleted, ct)
                ?? throw new ConflictAppException("Phiếu đã bị hủy hoặc không còn hợp lệ.");
            if (DepositVoucher(row)) throw new ConflictAppException("Phiếu cọc cần điều chỉnh từ khoản nhận cọc. Hãy từ chối yêu cầu sửa phiếu này.");
            if (await db.TreasuryEntries.AnyAsync(x => x.StoreId == StoreId && x.POSShiftCashTransactionId == row.Id, ct))
                throw new ConflictAppException("Phiếu đã liên kết sổ thu chi. Hủy liên kết trước khi duyệt điều chỉnh.");
            if (Version(row.RowVersion) != r.TransactionVersion) throw new ConflictAppException("Phiếu gốc đã thay đổi. Hãy từ chối yêu cầu này và lập yêu cầu mới.");
            var d = Deltas(r);
            if (shift.CashInTotal + d.In < 0 || shift.CashOutTotal + d.Out < 0) throw new ConflictAppException("Tổng thu/chi ca không khớp phiếu. Cần kiểm tra số liệu trước khi duyệt.");
            r.BeforeShiftJson = JsonSerializer.Serialize(Position(shift), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            shift.CashInTotal += d.In; shift.CashOutTotal += d.Out; shift.RecalcExpected();
            r.AppliedToClosedShift = shift.Status == POSShiftStatus.Closed;
            if (r.AppliedToClosedShift) shift.NeedsCashReconciliation = true;
            if (r.IsCancellation) { row.IsDeleted = true; row.DeletedBy = UserId; row.DeletedAtUtc = DateTime.UtcNow; }
            else { row.Type = r.NewType; row.Amount = r.NewAmount; row.Reason = r.NewReason; row.Note = r.NewNote; }
            r.AfterShiftJson = JsonSerializer.Serialize(Position(shift), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            // Closing slip snapshots and actual/received cash are immutable evidence; never overwrite them.
        }
        r.Status = next; r.ReviewedByUserId = UserId; r.ReviewedByName = await ActorName(ct); r.ReviewedAtUtc = DateTime.UtcNow; r.ReviewNote = note;
        await Save(ct); await tx.CommitAsync(ct);
    }

    public async Task ReconcileAsync(int shiftId, CashAdjustmentDecision input, CancellationToken ct)
    {
        await admin.RequireAsync(ct); var note = RequiredNote(input.Note);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var shift = await Shift(shiftId, true, ct);
        if (!shift.NeedsCashReconciliation) return;
        if (shift.Status != POSShiftStatus.Closed || Version(shift.RowVersion) != input.RowVersion) throw new ConflictAppException("Số liệu ca đã thay đổi. Hãy mở lại chi tiết để đối soát.");
        var rows = await Requests.Where(x => x.POSShiftId == shiftId && x.Status == POSCashAdjustmentStatus.Approved && x.AppliedToClosedShift && x.ReconciledAtUtc == null).ToListAsync(ct);
        var name = await ActorName(ct); var now = DateTime.UtcNow;
        foreach (var r in rows) { r.ReconciledAtUtc = now; r.ReconciledByUserId = UserId; r.ReconciledByName = name; r.ReconciliationNote = note; }
        var paymentRows = await db.Set<POSPaymentAdjustmentRequest>().Where(x => x.StoreId == StoreId && x.POSShiftId == shiftId
            && x.Status == POSCashAdjustmentStatus.Approved && x.AppliedToClosedShift && x.ReconciledAtUtc == null).ToListAsync(ct);
        foreach (var r in paymentRows) { r.ReconciledAtUtc = now; r.ReconciledByUserId = UserId; r.ReconciledByName = name; r.ReconciliationNote = note; }
        shift.NeedsCashReconciliation = false;
        await Save(ct); await tx.CommitAsync(ct);
    }
    private async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConflictAppException("Dữ liệu vừa thay đổi. Vui lòng tải lại trước khi thao tác."); }
        catch (DbUpdateException e) when (e.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 547 } sql
            && sql.Message.Contains("CK_POSShifts_ClosingCashExpected_NonNegative", StringComparison.OrdinalIgnoreCase))
        { throw new ConflictAppException("Database chưa được cập nhật để lưu tiền dự kiến âm. Vui lòng cập nhật Migrator trước khi duyệt lại. Yêu cầu chưa được áp dụng."); }
        catch (DbUpdateException e) when (e.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        { throw new ConflictAppException("Phiếu đã có yêu cầu chờ duyệt hoặc mã yêu cầu đã được sử dụng. Vui lòng tải lại danh sách."); }
    }
    internal static bool DepositVoucher(POSShiftCashTransaction row) => row.CustomerDepositEntryId.HasValue ||
        (row.Reason is "Nhận cọc khách hàng" or "Hoàn cọc khách hàng" && row.Note != null && row.Note.StartsWith("Phiếu cọc DC-", StringComparison.Ordinal));
}
