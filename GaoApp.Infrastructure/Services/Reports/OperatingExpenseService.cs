using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.Interfaces.Services.Reports;
using GaoApp.Application.Services.Reports;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Reports;

public sealed class OperatingExpenseService(AppDbContext db) : IOperatingExpenseService
{
    private int StoreId => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new InvalidOperationException("Cửa hàng chưa hợp lệ.");
    public async Task<ExpenseListDto> ListAsync(DateTime from, DateTime to, string? search, string? status, int page, CancellationToken ct)
    {
        if (from.Date > to.Date || (to.Date - from.Date).Days >= 366 || page < 1 || search?.Length > 100 ||
            status is not (null or "" or "all" or "draft" or "confirmed" or "voided")) throw new ArgumentException("Bộ lọc chi phí không hợp lệ.");
        var q = db.OperatingExpenses.AsNoTracking().Where(x => x.StoreId == StoreId && !x.IsDeleted &&
            x.RecognitionFrom <= to.Date && x.RecognitionTo >= from.Date);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Name.Contains(search.Trim()));
        if (status is "draft" or "confirmed" or "voided") q = q.Where(x => x.Status == status);
        var count = await q.CountAsync(ct);
        // Allocation needs interval endpoints; amount-only projections avoid loading notes for the full range.
        var totals = await q.Where(x => x.Status != "voided").Select(x => new OperatingExpense {
            Amount = x.Amount, Status = x.Status, RecognitionFrom = x.RecognitionFrom, RecognitionTo = x.RecognitionTo }).ToListAsync(ct);
        var rows = await q.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((int)Math.Min((long)(page - 1) * 25, int.MaxValue)).Take(25).ToListAsync(ct);
        return new(rows.Select(x => OperatingExpensePolicy.Row(x, from, to)).ToList(), count, page, 25,
            totals.Where(x => x.Status == "confirmed").Sum(x => OperatingExpensePolicy.Allocate(x, from, to)),
            totals.Where(x => x.Status == "draft").Sum(x => OperatingExpensePolicy.Allocate(x, from, to)));
    }
    public async Task<ExpenseRowDto> CreateAsync(ExpenseWriteDto request, CancellationToken ct)
    {
        OperatingExpensePolicy.Validate(request);
        if (request.ClientRequestId == Guid.Empty) throw new ArgumentException("Thiếu mã yêu cầu khoản chi.");
        var previous = await db.OperatingExpenses.SingleOrDefaultAsync(x => x.StoreId == StoreId && x.ClientRequestId == request.ClientRequestId, ct);
        if (previous is not null) return Existing(previous, request);
        var expense = new OperatingExpense { StoreId = StoreId, ClientRequestId = request.ClientRequestId };
        Apply(expense, request); db.OperatingExpenses.Add(expense);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) {
            db.Entry(expense).State = EntityState.Detached;
            var winner = await db.OperatingExpenses.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.ClientRequestId == request.ClientRequestId, ct);
            if (winner is null) throw;
            return Existing(winner, request);
        }
        return OperatingExpensePolicy.Row(expense);
    }
    private static ExpenseRowDto Existing(OperatingExpense x, ExpenseWriteDto r)
    {
        if (x.IsDeleted || x.Name != r.Name.Trim() || x.Amount != r.Amount || x.Category != r.Category ||
            x.RecognitionFrom != r.RecognitionFrom.Date || x.RecognitionTo != r.RecognitionTo.Date ||
            x.IsPaid != r.IsPaid || x.PaymentMethod != r.PaymentMethod || x.Note != r.Note?.Trim() || x.ReceiptReference != r.ReceiptReference?.Trim())
            throw new ConflictAppException("Mã yêu cầu đã dùng cho khoản chi khác. Tải lại trước khi nhập thêm.");
        return OperatingExpensePolicy.Row(x);
    }
    public async Task<ExpenseRowDto> UpdateAsync(int id, ExpenseWriteDto request, CancellationToken ct)
    {
        OperatingExpensePolicy.Validate(request);
        var x = await FindAsync(id, request.RowVersion, ct);
        if (x.Status != "draft") throw new ConflictAppException("Chỉ sửa khoản chi nháp. Khoản đã ghi nhận cần hủy và lập lại.");
        Apply(x, request); return await SaveAsync(x, ct);
    }
    public async Task<ExpenseRowDto> TransitionAsync(int id, string action, ExpenseActionDto request, CancellationToken ct)
    {
        var x = await FindAsync(id, request.RowVersion, ct);
        if (action == "confirm" && x.Status == "draft") {
            x.Status = "confirmed"; x.ConfirmedAtUtc = DateTime.UtcNow; x.ConfirmedBy = db.CurrentUserId;
        } else if (action == "void" && x.Status is "confirmed" or "draft") {
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
                throw new ArgumentException("Nhập lý do hủy khoản chi (tối đa 500 ký tự).");
            x.Status = "voided"; x.VoidReason = request.Reason.Trim();
        } else throw new ConflictAppException("Trạng thái khoản chi đã thay đổi. Tải lại danh sách để tiếp tục.");
        return await SaveAsync(x, ct);
    }
    private async Task<OperatingExpense> FindAsync(int id, string? version, CancellationToken ct)
    {
        var x = await db.OperatingExpenses.SingleOrDefaultAsync(x => x.StoreId == StoreId && !x.IsDeleted && x.Id == id, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy khoản chi trong cửa hàng.");
        byte[] expected;
        try { expected = Convert.FromBase64String(version ?? ""); } catch (FormatException) { throw new ArgumentException("Phiên bản khoản chi không hợp lệ."); }
        if (expected.Length == 0 || !expected.SequenceEqual(x.RowVersion ?? [])) throw new ConflictAppException("Khoản chi đã được thay đổi bởi người khác. Tải lại để tiếp tục.");
        db.Entry(x).Property(e => e.RowVersion).OriginalValue = expected;
        return x;
    }
    private async Task<ExpenseRowDto> SaveAsync(OperatingExpense x, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw new ConflictAppException("Khoản chi đã được thay đổi bởi người khác. Tải lại để tiếp tục."); }
        return OperatingExpensePolicy.Row(x);
    }
    private static void Apply(OperatingExpense x, ExpenseWriteDto r) {
        x.Name = r.Name.Trim(); x.Category = r.Category; x.Amount = r.Amount;
        x.RecognitionFrom = r.RecognitionFrom.Date; x.RecognitionTo = r.RecognitionTo.Date;
        x.IsPaid = r.IsPaid; x.PaymentMethod = r.PaymentMethod; x.ReceiptReference = r.ReceiptReference?.Trim(); x.Note = r.Note?.Trim();
    }
}
