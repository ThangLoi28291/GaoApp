using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Rewards.Vouchers;
using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Rewards;

public sealed class CustomerRewardVoucherRepository : ICustomerRewardVoucherRepository
{
    private const int VoucherDescriptionMaxLength = 500;

    private readonly AppDbContext _db;

    public CustomerRewardVoucherRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<CustomerRewardVoucher>> GetByCustomerAsync(
        int customerId,
        CustomerRewardVoucherStatus? status = null,
        CancellationToken ct = default)
    {
        var query = _db.CustomerRewardVouchers
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId);

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        return await query
            .OrderByDescending(x => x.IssuedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);
    }

    public async Task<int> CountAvailableAsync(int customerId, CancellationToken ct = default)
    {
        return await _db.CustomerRewardVouchers
            .CountAsync(x =>
                x.CustomerId == customerId &&
                x.Status == CustomerRewardVoucherStatus.Available,
                ct);
    }

    public async Task AddAsync(CustomerRewardVoucher voucher, CancellationToken ct = default)
    {
        await _db.CustomerRewardVouchers.AddAsync(voucher, ct);
    }

    public async Task<string> GenerateNextVoucherCodeAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var prefix = $"RV-{today}-";

        var countToday = await _db.CustomerRewardVouchers
            .CountAsync(x => x.VoucherCode.StartsWith(prefix), ct);

        var nextNumber = countToday + 1;

        string code;
        do
        {
            code = $"{prefix}{nextNumber:0000}";
            nextNumber++;
        }
        while (await _db.CustomerRewardVouchers.AnyAsync(x => x.VoucherCode == code, ct));

        return code;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<CustomerRewardVoucherListItemDto>> GetListAsync(
        CustomerRewardVoucherFilterDto filter,
        CancellationToken ct = default)
    {
        filter.Page = filter.Page <= 0 ? 1 : filter.Page;
        filter.PageSize = filter.PageSize <= 0 ? 20 : filter.PageSize;
        if (filter.PageSize > 100) filter.PageSize = 100;

        var query = _db.CustomerRewardVouchers
            .AsNoTracking()
            .Include(x => x.Customer)
            .Where(x => !x.IsDeleted);

        if (filter.CustomerId.HasValue && filter.CustomerId.Value > 0)
        {
            query = query.Where(x => x.CustomerId == filter.CustomerId.Value);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(x => x.Status == filter.Status.Value);
        }

        if (filter.FromDate.HasValue)
        {
            query = query.Where(x => x.IssuedAtUtc >= filter.FromDate.Value);
        }

        if (filter.ToDate.HasValue)
        {
            var toDate = filter.ToDate.Value.Date.AddDays(1);
            query = query.Where(x => x.IssuedAtUtc < toDate);
        }

        var keyword = (filter.Keyword ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(x =>
                x.VoucherCode.Contains(keyword) ||
                (x.Customer != null && x.Customer.Name.Contains(keyword)) ||
                (x.Customer != null && x.Customer.Phone != null && x.Customer.Phone.Contains(keyword)));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.IssuedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(x => new CustomerRewardVoucherListItemDto
            {
                Id = x.Id,
                VoucherCode = x.VoucherCode,
                CustomerId = x.CustomerId,
                CustomerName = x.Customer != null ? x.Customer.Name : "",
                CustomerPhone = x.Customer != null ? x.Customer.Phone : null,
                Value = x.Value,
                RequiredAmount = x.RequiredAmount,
                Status = x.Status.ToString(),
                IssuedAtUtc = x.IssuedAtUtc,
                UsedAtUtc = x.UsedAtUtc,
                UsedOrderId = x.UsedOrderId,
                Description = x.Description
            })
            .ToListAsync(ct);

        return new PagedResult<CustomerRewardVoucherListItemDto>
        {
            Items = items,
            Page = filter.Page,
            PageSize = filter.PageSize,
            TotalItems = total
        };
    }

    public async Task<CustomerRewardVoucher?> GetByIdAsync(
        int id,
        CancellationToken ct = default)
    {
        return await _db.CustomerRewardVouchers
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
    }

    public async Task<CustomerRewardVoucherDetailDto?> GetDetailAsync(
        int id,
        CancellationToken ct = default)
    {
        return await _db.CustomerRewardVouchers
            .AsNoTracking()
            .Include(x => x.Customer)
            .Where(x => x.Id == id && !x.IsDeleted)
            .Select(x => new CustomerRewardVoucherDetailDto
            {
                Id = x.Id,
                VoucherCode = x.VoucherCode,
                CustomerId = x.CustomerId,
                CustomerName = x.Customer != null ? x.Customer.Name : "",
                CustomerPhone = x.Customer != null ? x.Customer.Phone : null,
                Value = x.Value,
                RequiredAmount = x.RequiredAmount,
                Status = x.Status.ToString(),
                IssuedAtUtc = x.IssuedAtUtc,
                UsedAtUtc = x.UsedAtUtc,
                UsedOrderId = x.UsedOrderId,
                Description = x.Description,
                ReferenceCode = x.ReferenceCode
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Dictionary<CustomerRewardVoucherStatus, int>> CountByStatusAsync(
        CancellationToken ct = default)
    {
        return await _db.CustomerRewardVouchers
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .GroupBy(x => x.Status)
            .Select(x => new
            {
                Status = x.Key,
                Count = x.Count()
            })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
    }

    public async Task RestoreUsedVouchersByOrderIdAsync(
        int orderId,
        string reason,
        CancellationToken ct = default)
    {
        reason = (reason ?? string.Empty).Trim();

        var vouchers = await _db.CustomerRewardVouchers
            .Where(x =>
                x.UsedOrderId == orderId &&
                x.Status == CustomerRewardVoucherStatus.Used)
            .ToListAsync(ct);

        if (!vouchers.Any())
            return;

        var nowText = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

        foreach (var voucher in vouchers)
        {
            voucher.Status = CustomerRewardVoucherStatus.Available;
            voucher.UsedOrderId = null;
            voucher.UsedAtUtc = null;

            var note =
                $"[HOÀN VOUCHER - {nowText}] Hoàn lại do đơn #{orderId} bị void/refund. Lý do: {reason}";

            voucher.Description = AppendVoucherDescription(
                voucher.Description,
                note,
                VoucherDescriptionMaxLength);
        }
    }

    private static string? AppendVoucherDescription(
        string? current,
        string? newText,
        int maxLength = VoucherDescriptionMaxLength)
    {
        current = current?.Trim();
        newText = newText?.Trim();

        if (string.IsNullOrWhiteSpace(newText))
            return TrimText(current, maxLength);

        var value = string.IsNullOrWhiteSpace(current)
            ? newText
            : $"{current}{Environment.NewLine}{newText}";

        return TrimTextKeepNewest(value, maxLength);
    }

    private static string? TrimText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        if (value.Length <= maxLength)
            return value;

        if (maxLength <= 3)
            return value[..maxLength];

        return value[..(maxLength - 3)] + "...";
    }

    private static string TrimTextKeepNewest(string value, int maxLength)
    {
        value = value.Trim();

        if (value.Length <= maxLength)
            return value;

        if (maxLength <= 3)
            return value[^maxLength..];

        return "..." + value[^Math.Min(maxLength - 3, value.Length)..];
    }
    public async Task<CustomerRewardVoucher?> GetByCodeAsync(
    string code,
    CancellationToken ct = default)
    {
        code = (code ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(code))
            return null;

        return await _db.CustomerRewardVouchers
            .Include(x => x.Customer)
            .FirstOrDefaultAsync(x =>
                !x.IsDeleted &&
                x.VoucherCode == code,
                ct);
    }
}