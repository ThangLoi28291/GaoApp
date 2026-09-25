using GaoApp.Application.DTOs.Security.UserInStores;
using GaoApp.Application.Interfaces.Repositories.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Security;

public sealed class EmployeeIndexReadRepository : IEmployeeIndexReadRepository
{
    private const string AccentInsensitiveSearchCollation = "Latin1_General_100_CI_AI";

    private readonly AppDbContext _db;

    public EmployeeIndexReadRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<EmployeeIndexPageDto> QueryAsync(
        int storeId,
        int currentUserId,
        EmployeeIndexQueryRequest request,
        CancellationToken ct = default)
    {
        var scopedQuery = ApplyRole(
            ApplyKeyword(BuildBaseQuery(storeId), request.Keyword),
            request.RoleId);

        var summary = await scopedQuery
            .GroupBy(_ => 1)
            .Select(group => new EmployeeIndexSummaryDto
            {
                TotalEmployees = group.Count(),
                ActiveEmployees = group.Count(item => item.IsActive),
                InactiveEmployees = group.Count(item => !item.IsActive),
                DistinctRoleCount = group.Select(item => item.RoleId).Distinct().Count()
            })
            .FirstOrDefaultAsync(ct)
            ?? new EmployeeIndexSummaryDto();

        var filteredQuery = ApplyLifecycle(scopedQuery, request.Lifecycle);
        var totalItems = await filteredQuery.CountAsync(ct);
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling((double)totalItems / request.PageSize));
        var page = Math.Min(request.Page, totalPages);

        var items = await filteredQuery
            .OrderBy(item => item.User.FullName ?? item.User.UserName)
            .ThenBy(item => item.User.UserName)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(item => new EmployeeIndexItemDto
            {
                MappingId = item.Id,
                UserId = item.UserId,
                Username = item.User.UserName,
                FullName = item.User.FullName ?? string.Empty,
                Email = item.User.Email,
                PhoneNumber = item.PhoneNumber,
                RoleId = item.RoleId,
                RoleName = item.Role.Name,
                RoleCode = item.Role.Code,
                PositionName = item.PositionName,
                IsActive = item.IsActive,
                JoinedDate = item.JoinedDate,
                CreatedAtUtc = item.CreatedAtUtc,
                Note = item.Note,
                CanEdit = !item.User.IsHostAdmin,
                CanResetPassword = !item.User.IsHostAdmin,
                CanToggleActive = !item.User.IsHostAdmin && item.UserId != currentUserId,
                ActionLockReason = item.User.IsHostAdmin
                    ? "Tài khoản Host Admin được bảo vệ."
                    : item.UserId == currentUserId
                        ? "Không thể tự khóa tài khoản đang đăng nhập."
                        : null
            })
            .ToListAsync(ct);

        var roleOptions = await _db.Roles
            .AsNoTracking()
            .Where(role => role.StoreId == storeId && !role.IsDeleted)
            .OrderBy(role => role.Name)
            .ThenBy(role => role.Code)
            .Select(role => new EmployeeIndexRoleOptionDto
            {
                RoleId = role.Id,
                Name = role.Name,
                Code = role.Code
            })
            .ToListAsync(ct);

        return new EmployeeIndexPageDto
        {
            Page = page,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            Summary = summary,
            RoleOptions = roleOptions,
            Items = items
        };
    }

    private IQueryable<UserInStore> BuildBaseQuery(int storeId)
        => _db.UserInStores
            .AsNoTracking()
            .Where(item => item.StoreId == storeId && !item.IsDeleted);

    private IQueryable<UserInStore> ApplyKeyword(
        IQueryable<UserInStore> query,
        string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return query;

        var search = keyword.Trim();
        if (_db.Database.IsRelational())
        {
            var accentInsensitiveSearch = NormalizeVietnameseD(search);

            return query.Where(item =>
                EF.Functions.Collate(
                    (item.User.FullName ?? string.Empty).Replace("Đ", "D").Replace("đ", "d"),
                    AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch)
                || EF.Functions.Collate(
                    item.User.UserName.Replace("Đ", "D").Replace("đ", "d"),
                    AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch)
                || EF.Functions.Collate(
                    (item.User.Email ?? string.Empty).Replace("Đ", "D").Replace("đ", "d"),
                    AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch)
                || EF.Functions.Collate(
                    (item.PhoneNumber ?? string.Empty).Replace("Đ", "D").Replace("đ", "d"),
                    AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch)
                || EF.Functions.Collate(
                    (item.PositionName ?? string.Empty).Replace("Đ", "D").Replace("đ", "d"),
                    AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch));
        }

        return query.Where(item =>
            (item.User.FullName != null && item.User.FullName.Contains(search))
            || item.User.UserName.Contains(search)
            || (item.User.Email != null && item.User.Email.Contains(search))
            || (item.PhoneNumber != null && item.PhoneNumber.Contains(search))
            || (item.PositionName != null && item.PositionName.Contains(search)));
    }

    private static string NormalizeVietnameseD(string value)
        => value.Replace("Đ", "D").Replace("đ", "d");

    private static IQueryable<UserInStore> ApplyRole(
        IQueryable<UserInStore> query,
        int? roleId)
        => roleId.HasValue
            ? query.Where(item => item.RoleId == roleId.Value)
            : query;

    private static IQueryable<UserInStore> ApplyLifecycle(
        IQueryable<UserInStore> query,
        string? lifecycle)
        => lifecycle switch
        {
            EmployeeIndexLifecycles.Active => query.Where(item => item.IsActive),
            EmployeeIndexLifecycles.Inactive => query.Where(item => !item.IsActive),
            _ => query
        };
}
