using GaoApp.Application.DTOs.Security.Roles;
using GaoApp.Application.Interfaces.Repositories.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Security;

public sealed class RoleIndexReadRepository : IRoleIndexReadRepository
{
    private const string AccentInsensitiveSearchCollation = "Latin1_General_100_CI_AI";

    private readonly AppDbContext _db;

    public RoleIndexReadRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<RoleIndexPageDto> QueryAsync(
        int storeId,
        RoleIndexQueryRequest request,
        CancellationToken ct = default)
    {
        var scopedQuery = ApplyType(
            ApplyKeyword(BuildBaseQuery(storeId), request.Keyword),
            request.Type);

        var summary = await scopedQuery
            .GroupBy(_ => 1)
            .Select(group => new RoleIndexSummaryDto
            {
                TotalRoles = group.Count(),
                ActiveRoles = group.Count(item => !item.IsDeleted),
                InactiveRoles = group.Count(item => item.IsDeleted),
                SystemRoles = group.Count(item => item.IsSystemRole)
            })
            .FirstOrDefaultAsync(ct)
            ?? new RoleIndexSummaryDto();

        var filteredQuery = ApplyLifecycle(scopedQuery, request.Lifecycle);
        var totalItems = await filteredQuery.CountAsync(ct);
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling((double)totalItems / request.PageSize));
        var page = Math.Min(request.Page, totalPages);

        var items = await filteredQuery
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Code)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(item => new RoleIndexItemDto
            {
                RoleId = item.Id,
                Code = item.Code,
                Name = item.Name,
                IsSystemRole = item.IsSystemRole,
                IsActive = !item.IsDeleted,
                PermissionCount = item.IsDeleted
                    ? 0
                    : _db.RolePermissions.Count(permission =>
                        permission.RoleId == item.Id
                        && permission.Role.StoreId == storeId
                        && !permission.Role.IsDeleted),
                ActiveUserCount = _db.UserInStores.Count(user =>
                    user.StoreId == storeId
                    && user.RoleId == item.Id
                    && !user.IsDeleted
                    && user.IsActive),
                AssignedUserCount = _db.UserInStores.Count(user =>
                    user.StoreId == storeId
                    && user.RoleId == item.Id
                    && !user.IsDeleted),
                CreatedAtUtc = item.CreatedAtUtc
            })
            .ToListAsync(ct);

        return new RoleIndexPageDto
        {
            Page = page,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            Summary = summary,
            Items = items
        };
    }

    private IQueryable<Role> BuildBaseQuery(int storeId)
        => _db.Roles
            .AsNoTracking()
            .Where(item => item.StoreId == storeId);

    private IQueryable<Role> ApplyKeyword(
        IQueryable<Role> query,
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
                    item.Name.Replace("Đ", "D").Replace("đ", "d"),
                    AccentInsensitiveSearchCollation)
                    .Contains(accentInsensitiveSearch)
                || EF.Functions.Collate(
                    item.Code.Replace("Đ", "D").Replace("đ", "d"),
                    AccentInsensitiveSearchCollation)
                    .Contains(accentInsensitiveSearch));
        }

        return query.Where(item =>
            item.Name.Contains(search) || item.Code.Contains(search));
    }

    private static IQueryable<Role> ApplyType(
        IQueryable<Role> query,
        string? type)
        => type switch
        {
            RoleIndexTypes.System => query.Where(item => item.IsSystemRole),
            RoleIndexTypes.Custom => query.Where(item => !item.IsSystemRole),
            _ => query
        };

    private static IQueryable<Role> ApplyLifecycle(
        IQueryable<Role> query,
        string? lifecycle)
        => lifecycle switch
        {
            RoleIndexLifecycles.Active => query.Where(item => !item.IsDeleted),
            RoleIndexLifecycles.Inactive => query.Where(item => item.IsDeleted),
            _ => query
        };
}
