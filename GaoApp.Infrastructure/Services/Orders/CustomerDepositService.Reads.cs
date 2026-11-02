using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Orders;

public sealed partial class CustomerDepositService
{
    private async Task<DepositListDto> GetListAsync(int? customerId, string? search, int page, string? status, CancellationToken ct)
    {
        var storeId = StoreId;
        var list = new DepositListDto
        {
            Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            Status = status is "active" or "closed" ? status : null
        };
        var totals = db.Set<CustomerDepositEntry>().AsNoTracking().Where(e => e.StoreId == storeId)
            .GroupBy(e => e.CustomerDepositId).Select(g => new
            {
                Id = g.Key,
                Received = (decimal?)g.Sum(e => e.Kind == "Receive" ? e.Amount : 0),
                Used = (decimal?)g.Sum(e => e.Kind == "Apply" ? -e.Amount : 0),
                Refunded = (decimal?)g.Sum(e => e.Kind == "Refund" ? -e.Amount : 0)
            });
        var rows = from d in db.Set<CustomerDeposit>().AsNoTracking()
                   join c in db.Customers.Where(c => c.StoreId == storeId) on d.CustomerId equals c.Id
                   join t in totals on d.Id equals t.Id into amounts
                   from t in amounts.DefaultIfEmpty()
                   where d.StoreId == storeId && (!customerId.HasValue || d.CustomerId == customerId)
                   select new
                   {
                       d.Id, CustomerId = c.Id, CustomerName = c.Name, c.Phone, At = d.CreatedAtUtc,
                       d.Purpose, d.ExpectedDeliveryDate, Received = t.Received ?? 0,
                       Used = t.Used ?? 0, Refunded = t.Refunded ?? 0, d.Balance
                   };
        if (list.Search is { } term)
        {
            var code = term.StartsWith("PCOC-", StringComparison.OrdinalIgnoreCase) ? term[5..]
                : term.StartsWith("DC-", StringComparison.OrdinalIgnoreCase) ? term[3..] : term;
            var depositId = int.TryParse(code, out var id) ? id : 0;
            rows = rows.Where(d => d.Id == depositId || d.CustomerName.Contains(term)
                || (d.Phone != null && d.Phone.Contains(term)) || d.Purpose.Contains(term));
        }
        if (list.Status == "active") rows = rows.Where(d => d.Balance > 0);
        if (list.Status == "closed") rows = rows.Where(d => d.Balance == 0);
        var summary = await rows.GroupBy(d => 1).Select(g => new
        {
            TotalCount = g.Count(), ActiveCount = g.Count(d => d.Balance > 0),
            Balance = g.Sum(d => d.Balance), Received = g.Sum(d => d.Received),
            UsedAndRefunded = g.Sum(d => d.Used + d.Refunded)
        }).SingleOrDefaultAsync(ct);
        if (summary != null)
        {
            list.TotalCount = summary.TotalCount;
            list.ActiveCount = summary.ActiveCount;
            list.Balance = summary.Balance;
            list.Received = summary.Received;
            list.UsedAndRefunded = summary.UsedAndRefunded;
        }
        list.Page = Math.Clamp(page, 1, list.TotalPages);
        list.Rows = await rows.OrderByDescending(d => d.At).ThenByDescending(d => d.Id)
            .Skip((list.Page - 1) * list.PageSize).Take(list.PageSize)
            .Select(d => new DepositListItemDto(d.Id, d.CustomerId, d.CustomerName, d.Phone, d.At,
                d.Purpose, d.ExpectedDeliveryDate, d.Received, d.Used, d.Refunded, d.Balance)).ToListAsync(ct);
        return list;
    }

    public async Task<DepositReceiptDto> GetReceiptAsync(int depositId, CancellationToken ct)
    {
        var storeId = StoreId;
        return await (from d in db.Set<CustomerDeposit>().AsNoTracking()
                      join e in db.Set<CustomerDepositEntry>() on d.Id equals e.CustomerDepositId
                      join c in db.Customers.Where(c => c.StoreId == storeId) on d.CustomerId equals c.Id
                      join s in db.Stores on d.StoreId equals s.Id
                      join u in db.Users on e.CreatedBy equals u.Id into users
                      from u in users.DefaultIfEmpty()
                      join b in db.StoreBankAccounts.Where(b => b.StoreId == storeId) on e.StoreBankAccountId equals b.Id into banks
                      from b in banks.DefaultIfEmpty()
                      where d.StoreId == storeId && e.StoreId == storeId && d.Id == depositId && e.Kind == "Receive"
                      select new DepositReceiptDto(d.Id, s.ReceiptName ?? s.Name, s.ReceiptAddress, s.ReceiptPhone,
                          c.Id, c.Name, c.Phone, c.Address, d.Purpose, d.ExpectedDeliveryDate, e.CreatedAtUtc,
                          e.Amount, d.Balance, e.Method, b == null ? null : b.BankName + " · " + b.AccountNumber + " · " + b.AccountName,
                          e.Reference, e.Note, e.POSShiftId, e.CreatedBy, u == null ? null : u.FullName ?? u.UserName))
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundAppException("Không tìm thấy phiếu cọc tại cửa hàng này.");
    }
}
