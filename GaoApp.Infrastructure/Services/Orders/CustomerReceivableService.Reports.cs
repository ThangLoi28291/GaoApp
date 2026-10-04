using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Orders;

public sealed partial class CustomerReceivableService
{
    public async Task<DebtCollectionReportDto> GetCollectionReportAsync(DebtCollectionQuery query, CancellationToken ct, bool export = false)
    {
        var today = DateTime.UtcNow.AddHours(7).Date;
        query.From = query.From?.Date ?? new DateTime(today.Year, today.Month, 1);
        query.To = query.To?.Date ?? today;
        if (query.From > query.To || query.From < new DateTime(2000, 1, 1) || query.To > new DateTime(9999, 12, 30))
            throw new BusinessRuleException("Khoảng ngày không hợp lệ. Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.");
        if (query.Method.HasValue && query.Method is not (PaymentMethod.Cash or PaymentMethod.BankTransfer))
            throw new BusinessRuleException("Phương thức thu nợ không hợp lệ.");
        query.Search = query.Search?.Trim();
        if (query.Search?.Length > 100) throw new BusinessRuleException("Từ khóa tối đa 100 ký tự.");
        if (query.CustomerId is <= 0 || query.UserId is <= 0 || query.ShiftId is <= 0)
            throw new BusinessRuleException("Mã khách hàng, người thu hoặc ca không hợp lệ.");
        var storeId = StoreId;
        // Dates entered in the store's UTC+7 calendar; inclusive end date, exclusive UTC boundary.
        var fromUtc = DateTime.SpecifyKind(query.From.Value.AddHours(-7), DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(query.To.Value.AddDays(1).AddHours(-7), DateTimeKind.Utc);
        var receipts = db.Set<CustomerDebtReceipt>().AsNoTracking().Where(x => x.StoreId == storeId);
        var report = new DebtCollectionReportDto { Query = query };
        if (query.CustomerId.HasValue)
            report.CustomerName = await db.Customers.Where(x => x.StoreId == storeId && x.Id == query.CustomerId)
                .Select(x => x.Name).SingleOrDefaultAsync(ct)
                ?? throw new BusinessRuleException("Không tìm thấy khách hàng tại cửa hàng này.");
        report.Collectors = await (from r in receipts
            join u in db.Users on r.CreatedBy equals u.Id
            select new { u.Id, Name = u.FullName ?? u.UserName }).Distinct().OrderBy(x => x.Name)
            .Select(x => new DebtCollectorDto(x.Id, x.Name)).ToListAsync(ct);
        receipts = receipts.Where(x => x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc < toUtc);
        if (query.CustomerId.HasValue) receipts = receipts.Where(x => x.CustomerId == query.CustomerId);
        if (query.Method.HasValue) receipts = receipts.Where(x => x.Method == query.Method);
        if (query.UserId.HasValue) receipts = receipts.Where(x => x.CreatedBy == query.UserId);
        if (query.ShiftId.HasValue) receipts = receipts.Where(x => x.POSShiftId == query.ShiftId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search;
            receipts = receipts.Where(r => (r.Reference != null && r.Reference.Contains(term)) ||
                db.Customers.Any(c => c.StoreId == storeId && c.Id == r.CustomerId &&
                    (c.Name.Contains(term) || (c.Phone != null && c.Phone.Contains(term)) || (c.Code != null && c.Code.Contains(term)))));
        }
        report.TotalReceipts = await receipts.CountAsync(ct);
        report.CustomerCount = await receipts.Select(x => x.CustomerId).Distinct().CountAsync(ct);
        report.CashTotal = await receipts.Where(x => x.Method == PaymentMethod.Cash).SumAsync(x => x.Amount, ct);
        report.BankTotal = await receipts.Where(x => x.Method == PaymentMethod.BankTransfer).SumAsync(x => x.Amount, ct);
        query.Page = Math.Clamp(query.Page, 1, Math.Max(1, (report.TotalReceipts + report.PageSize - 1) / report.PageSize));
        if (export && report.TotalReceipts > 50000)
            throw new BusinessRuleException("Báo cáo vượt 50.000 phiếu. Thu hẹp khoảng ngày hoặc bộ lọc để xuất Excel.");
        var ordered = receipts.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id);
        report.Rows = await ReceiptRows(export ? ordered : ordered.Skip((query.Page - 1) * report.PageSize).Take(report.PageSize)).ToListAsync(ct);
        return report;
    }

    public async Task<DebtReceiptDetailDto> GetReceiptAsync(int receiptId, CancellationToken ct)
    {
        var storeId = StoreId;
        var receipt = await ReceiptRows(db.Set<CustomerDebtReceipt>().AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Id == receiptId)).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundAppException("Không tìm thấy phiếu thu công nợ tại cửa hàng này.");
        var allocations = await (from e in db.Set<CustomerReceivableEntry>().AsNoTracking()
            join o in db.Orders on e.OrderId equals o.Id
            where e.StoreId == storeId && o.StoreId == storeId && e.ReceiptId == receiptId && e.Kind == "Collection"
            orderby e.Id
            select new DebtReceiptAllocationDto(o.Id, o.OrderNumber, -e.Amount)).ToListAsync(ct);
        var storeName = await db.Stores.Where(x => x.Id == storeId).Select(x => x.Name).SingleAsync(ct);
        return new DebtReceiptDetailDto(storeName, receipt, allocations);
    }

    private IQueryable<DebtCollectionRowDto> ReceiptRows(IQueryable<CustomerDebtReceipt> receipts)
    {
        var storeId = StoreId;
        return from r in receipts
            join c in db.Customers.Where(x => x.StoreId == storeId) on r.CustomerId equals c.Id into customers
            from c in customers.DefaultIfEmpty()
            join u in db.Users on r.CreatedBy equals u.Id into users
            from u in users.DefaultIfEmpty()
            join b in db.StoreBankAccounts.Where(x => x.StoreId == storeId) on r.StoreBankAccountId equals b.Id into banks
            from b in banks.DefaultIfEmpty()
            select new DebtCollectionRowDto(r.Id, r.CreatedAtUtc, r.CustomerId,
                c == null ? "Khách #" + r.CustomerId : c.Name, c == null ? null : c.Phone,
                r.Amount, r.Method, b == null ? null : b.BankName + " · " + b.AccountNumber,
                r.Reference, r.Note, r.CreatedBy, u == null ? null : u.FullName ?? u.UserName, r.POSShiftId);
    }
}
