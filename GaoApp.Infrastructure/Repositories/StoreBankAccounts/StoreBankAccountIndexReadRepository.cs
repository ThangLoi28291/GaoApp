using GaoApp.Application.DTOs.StoreBankAccounts;
using GaoApp.Application.Interfaces.Repositories.StoreBankAccounts;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.StoreBankAccounts;

public sealed class StoreBankAccountIndexReadRepository
    : IStoreBankAccountIndexReadRepository
{
    private const string AccentInsensitiveSearchCollation = "Latin1_General_100_CI_AI";

    private readonly AppDbContext _db;

    public StoreBankAccountIndexReadRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<StoreBankAccountIndexPageDto> QueryAsync(
        int storeId,
        StoreBankAccountIndexQueryRequest request,
        CancellationToken ct = default)
    {
        var summaryScope = ApplyConfirmMode(
            ApplyQrMode(
                ApplyKeyword(BuildBaseQuery(storeId), request.Keyword),
                request.QrMode),
            request.ConfirmMode);

        var summary = await summaryScope
            .GroupBy(_ => 1)
            .Select(group => new StoreBankAccountIndexSummaryDto
            {
                TotalAccounts = group.Count(),
                ActiveAccounts = group.Count(item => item.IsActive),
                InactiveAccounts = group.Count(item => !item.IsActive),
                DefaultAccounts = group.Count(item => item.IsDefault),
                ActiveDefaultAccounts = group.Count(item => item.IsDefault && item.IsActive),
                InactiveDefaultAccounts = group.Count(item => item.IsDefault && !item.IsActive)
            })
            .FirstOrDefaultAsync(ct)
            ?? new StoreBankAccountIndexSummaryDto();

        var filteredQuery = ApplyDefaultRole(
            ApplyLifecycle(summaryScope, request.Lifecycle),
            request.DefaultRole);

        var totalItems = await filteredQuery.CountAsync(ct);
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling((double)totalItems / request.PageSize));
        var page = Math.Min(request.Page, totalPages);

        var items = await filteredQuery
            .OrderByDescending(item => item.IsDefault)
            .ThenByDescending(item => item.IsActive)
            .ThenBy(item => item.BankName)
            .ThenBy(item => item.AccountNumber)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(item => new StoreBankAccountIndexItemDto
            {
                BankAccountId = item.Id,
                BankCode = item.BankCode,
                BankName = item.BankName,
                AccountNumber = item.AccountNumber,
                AccountName = item.AccountName,
                IsDefault = item.IsDefault,
                IsActive = item.IsActive,
                VietQrBankBin = item.VietQrBankBin,
                NoteTemplate = item.NoteTemplate,
                QrRenderMode = item.QrRenderMode,
                ConfirmMode = item.ConfirmMode,
                ProviderCode = item.ProviderCode
            })
            .ToListAsync(ct);

        return new StoreBankAccountIndexPageDto
        {
            Page = page,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            Summary = summary,
            Items = items
        };
    }

    private IQueryable<StoreBankAccount> BuildBaseQuery(int storeId)
        => _db.StoreBankAccounts
            .AsNoTracking()
            .Where(item => item.StoreId == storeId);

    private IQueryable<StoreBankAccount> ApplyKeyword(
        IQueryable<StoreBankAccount> query,
        string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return query;

        var search = keyword.Trim();
        if (_db.Database.IsRelational())
        {
            var accentInsensitiveSearch = search
                .Replace('Đ', 'D')
                .Replace('đ', 'd');

            return query.Where(item =>
                EF.Functions.Collate(
                    item.BankName.Replace("Đ", "D").Replace("đ", "d"),
                    AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch)
                || EF.Functions.Collate(
                    item.BankCode.Replace("Đ", "D").Replace("đ", "d"),
                    AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch)
                || EF.Functions.Collate(
                    item.AccountNumber,
                    AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch)
                || EF.Functions.Collate(
                    item.AccountName.Replace("Đ", "D").Replace("đ", "d"),
                    AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch)
                || EF.Functions.Collate(
                    item.ProviderCode.Replace("Đ", "D").Replace("đ", "d"),
                    AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch));
        }

        return query.Where(item =>
            item.BankName.Contains(search)
            || item.BankCode.Contains(search)
            || item.AccountNumber.Contains(search)
            || item.AccountName.Contains(search)
            || item.ProviderCode.Contains(search));
    }

    private static IQueryable<StoreBankAccount> ApplyQrMode(
        IQueryable<StoreBankAccount> query,
        string? qrMode)
        => qrMode switch
        {
            StoreBankAccountIndexQrModes.LocalEmvQr => query.Where(
                item => item.QrRenderMode == BankQrRenderMode.LocalEmvQr),
            StoreBankAccountIndexQrModes.VietQrQuickLink => query.Where(
                item => item.QrRenderMode == BankQrRenderMode.VietQrQuickLink),
            StoreBankAccountIndexQrModes.ProviderApi => query.Where(
                item => item.QrRenderMode == BankQrRenderMode.ProviderApi),
            _ => query
        };

    private static IQueryable<StoreBankAccount> ApplyConfirmMode(
        IQueryable<StoreBankAccount> query,
        string? confirmMode)
        => confirmMode switch
        {
            StoreBankAccountIndexConfirmModes.Manual => query.Where(
                item => item.ConfirmMode == BankQrConfirmMode.Manual),
            StoreBankAccountIndexConfirmModes.Callback => query.Where(
                item => item.ConfirmMode == BankQrConfirmMode.Callback),
            StoreBankAccountIndexConfirmModes.Polling => query.Where(
                item => item.ConfirmMode == BankQrConfirmMode.Polling),
            _ => query
        };

    private static IQueryable<StoreBankAccount> ApplyLifecycle(
        IQueryable<StoreBankAccount> query,
        string? lifecycle)
        => lifecycle switch
        {
            StoreBankAccountIndexLifecycles.Active => query.Where(item => item.IsActive),
            StoreBankAccountIndexLifecycles.Inactive => query.Where(item => !item.IsActive),
            _ => query
        };

    private static IQueryable<StoreBankAccount> ApplyDefaultRole(
        IQueryable<StoreBankAccount> query,
        string? defaultRole)
        => defaultRole switch
        {
            StoreBankAccountIndexDefaults.Default => query.Where(item => item.IsDefault),
            StoreBankAccountIndexDefaults.NotDefault => query.Where(item => !item.IsDefault),
            _ => query
        };
}
