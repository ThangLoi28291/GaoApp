using GaoApp.Application.DTOs.Rewards.Vouchers;
using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Rewards;

public sealed class RewardVoucherIndexReadRepository : IRewardVoucherIndexReadRepository
{
    private const string AccentInsensitiveSearchCollation = "Latin1_General_100_CI_AI";
    private readonly AppDbContext _db;

    public RewardVoucherIndexReadRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<RewardVoucherIndexPageDto> QueryAsync(
        int storeId,
        RewardVoucherIndexQueryRequest request,
        CancellationToken ct = default)
    {
        var summaryScope = ApplyIssuedDateRange(
            ApplyKeyword(BuildBaseQuery(storeId), request.Keyword),
            request.FromDate,
            request.ToDate);

        var summary = await summaryScope
            .GroupBy(_ => 1)
            .Select(group => new RewardVoucherIndexSummaryDto
            {
                TotalVouchers = group.Count(),
                AvailableVouchers = group.Count(item => item.Status == CustomerRewardVoucherStatus.Available),
                UsedVouchers = group.Count(item => item.Status == CustomerRewardVoucherStatus.Used),
                UnavailableVouchers = group.Count(item =>
                    item.Status == CustomerRewardVoucherStatus.Cancelled
                    || item.Status == CustomerRewardVoucherStatus.Expired
                    || item.Status == CustomerRewardVoucherStatus.Locked)
            })
            .FirstOrDefaultAsync(ct)
            ?? new RewardVoucherIndexSummaryDto();

        var filteredQuery = ApplyStatus(summaryScope, request.Status);
        var totalItems = await filteredQuery.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)totalItems / request.PageSize));
        var page = Math.Min(request.Page, totalPages);

        var items = await filteredQuery
            .OrderByDescending(item => item.IssuedAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(item => new RewardVoucherIndexItemDto
            {
                VoucherId = item.Id,
                VoucherCode = item.VoucherCode,
                ReferenceCode = item.ReferenceCode,
                CustomerName = item.Customer != null ? item.Customer.Name : string.Empty,
                CustomerCode = item.Customer != null ? item.Customer.Code : null,
                CustomerPhone = item.Customer != null ? item.Customer.Phone : null,
                CustomerEmail = item.Customer != null ? item.Customer.Email : null,
                Value = item.Value,
                RequiredAmount = item.RequiredAmount,
                Status = item.Status,
                IssuedAtUtc = item.IssuedAtUtc,
                UsedAtUtc = item.UsedAtUtc,
                IsLinkedToOrder = item.UsedOrderId.HasValue
            })
            .ToListAsync(ct);

        return new RewardVoucherIndexPageDto
        {
            Page = page,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            Summary = summary,
            Items = items
        };
    }

    public Task<RewardVoucherIndexQuickViewDto?> GetQuickViewAsync(
        int storeId,
        int voucherId,
        CancellationToken ct = default)
        => BuildBaseQuery(storeId)
            .Where(item => item.Id == voucherId)
            .Select(item => new RewardVoucherIndexQuickViewDto
            {
                VoucherId = item.Id,
                VoucherCode = item.VoucherCode,
                ReferenceCode = item.ReferenceCode,
                CustomerName = item.Customer != null ? item.Customer.Name : string.Empty,
                CustomerCode = item.Customer != null ? item.Customer.Code : null,
                CustomerPhone = item.Customer != null ? item.Customer.Phone : null,
                CustomerEmail = item.Customer != null ? item.Customer.Email : null,
                Value = item.Value,
                RequiredAmount = item.RequiredAmount,
                Status = item.Status,
                IssuedAtUtc = item.IssuedAtUtc,
                UsedAtUtc = item.UsedAtUtc,
                IsLinkedToOrder = item.UsedOrderId.HasValue,
                Description = item.Description
            })
            .FirstOrDefaultAsync(ct);

    private IQueryable<CustomerRewardVoucher> BuildBaseQuery(int storeId)
        => _db.CustomerRewardVouchers
            .AsNoTracking()
            .Where(item => item.StoreId == storeId);

    private IQueryable<CustomerRewardVoucher> ApplyKeyword(
        IQueryable<CustomerRewardVoucher> query,
        string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return query;

        var search = keyword.Trim();
        if (_db.Database.IsRelational())
        {
            var accentInsensitiveSearch = search.Replace('Đ', 'D').Replace('đ', 'd');

            return query.Where(item =>
                EF.Functions.Collate(item.VoucherCode.Replace("Đ", "D").Replace("đ", "d"), AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch)
                || EF.Functions.Collate((item.ReferenceCode ?? string.Empty).Replace("Đ", "D").Replace("đ", "d"), AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch)
                || (item.Customer != null && EF.Functions.Collate(item.Customer.Name.Replace("Đ", "D").Replace("đ", "d"), AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch))
                || (item.Customer != null && EF.Functions.Collate((item.Customer.Code ?? string.Empty).Replace("Đ", "D").Replace("đ", "d"), AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch))
                || (item.Customer != null && EF.Functions.Collate(item.Customer.Phone ?? string.Empty, AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch))
                || (item.Customer != null && EF.Functions.Collate((item.Customer.Email ?? string.Empty).Replace("Đ", "D").Replace("đ", "d"), AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch)));
        }

        return query.Where(item =>
            item.VoucherCode.Contains(search)
            || (item.ReferenceCode != null && item.ReferenceCode.Contains(search))
            || (item.Customer != null && item.Customer.Name.Contains(search))
            || (item.Customer != null && item.Customer.Code != null && item.Customer.Code.Contains(search))
            || (item.Customer != null && item.Customer.Phone != null && item.Customer.Phone.Contains(search))
            || (item.Customer != null && item.Customer.Email != null && item.Customer.Email.Contains(search)));
    }

    private static IQueryable<CustomerRewardVoucher> ApplyIssuedDateRange(
        IQueryable<CustomerRewardVoucher> query,
        DateTime? fromDate,
        DateTime? toDate)
    {
        if (fromDate.HasValue)
        {
            var fromInclusive = fromDate.Value.Date;
            query = query.Where(item => item.IssuedAtUtc >= fromInclusive);
        }

        if (toDate.HasValue)
        {
            var toExclusive = toDate.Value.Date.AddDays(1);
            query = query.Where(item => item.IssuedAtUtc < toExclusive);
        }

        return query;
    }

    private static IQueryable<CustomerRewardVoucher> ApplyStatus(
        IQueryable<CustomerRewardVoucher> query,
        string? status)
        => status switch
        {
            RewardVoucherIndexStatuses.Available => query.Where(item => item.Status == CustomerRewardVoucherStatus.Available),
            RewardVoucherIndexStatuses.Used => query.Where(item => item.Status == CustomerRewardVoucherStatus.Used),
            RewardVoucherIndexStatuses.Cancelled => query.Where(item => item.Status == CustomerRewardVoucherStatus.Cancelled),
            RewardVoucherIndexStatuses.Expired => query.Where(item => item.Status == CustomerRewardVoucherStatus.Expired),
            RewardVoucherIndexStatuses.Locked => query.Where(item => item.Status == CustomerRewardVoucherStatus.Locked),
            _ => query
        };
}
