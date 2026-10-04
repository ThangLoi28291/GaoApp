using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.DTOs.AdminMenus;
using GaoApp.Application.Interfaces.Services.AdminMenus;
using GaoApp.Application.Services.AdminMenus;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace GaoApp.Infrastructure.Repositories.AdminMenus;

public sealed class AdminMenuVisibilityService(AppDbContext db) : IAdminMenuVisibilityService
{
    public async Task<List<MenuVisibilitySubject>> GetSubjectsAsync(int storeId, CancellationToken ct = default)
    {
        var personalIds = await db.Set<AdminMenuVisibilitySetting>().AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted && x.UserInStoreId != null && x.HiddenMenuIdsJson != null)
            .Select(x => x.UserInStoreId!.Value).ToListAsync(ct);
        var roles = await db.Roles.AsNoTracking().Where(x => x.StoreId == storeId && !x.IsDeleted)
            .OrderBy(x => x.Name).Select(x => new MenuVisibilitySubject("role", x.Id, x.Name, x.Code,
                x.UserInStores.Count(u => u.StoreId == storeId && !u.IsDeleted && !u.User.IsDeleted),
                x.UserInStores.Count(u => u.StoreId == storeId && !u.IsDeleted && !u.User.IsDeleted && personalIds.Contains(u.Id)), false)).ToListAsync(ct);
        var users = await db.UserInStores.AsNoTracking().Where(x => x.StoreId == storeId && !x.IsDeleted && !x.User.IsDeleted && x.Role.StoreId == storeId && !x.Role.IsDeleted)
            .OrderBy(x => x.User.FullName).ThenBy(x => x.User.UserName)
            .Select(x => new MenuVisibilitySubject("employee", x.Id, x.User.FullName ?? x.User.UserName,
                x.Role.Name + " · " + x.User.UserName + (x.IsActive && x.User.IsActive ? "" : " · Ngừng hoạt động"),
                1, 0, personalIds.Contains(x.Id))).ToListAsync(ct);
        return roles.Concat(users).ToList();
    }

    private async Task<(Role Role, UserInStore? Employee)> Target(int storeId, string type, int id, CancellationToken ct)
    {
        if (type == "employee")
        {
            var employee = await db.UserInStores.Include(x => x.User).Include(x => x.Role).ThenInclude(x => x.RolePermissions).ThenInclude(x => x.Permission)
                .SingleOrDefaultAsync(x => x.StoreId == storeId && x.Id == id && !x.IsDeleted && !x.User.IsDeleted && x.Role.StoreId == storeId && !x.Role.IsDeleted, ct);
            if (employee != null) return (employee.Role, employee);
        }
        else if (type == "role")
        {
            var role = await db.Roles.Include(x => x.RolePermissions).ThenInclude(x => x.Permission)
                .SingleOrDefaultAsync(x => x.StoreId == storeId && x.Id == id && !x.IsDeleted, ct);
            if (role != null) return (role, null);
        }
        throw new KeyNotFoundException("Không tìm thấy nhân viên hoặc nhóm trong cửa hàng này.");
    }

    public async Task<MenuVisibilityWorkspace> GetAsync(int storeId, string type, int id, CancellationToken ct = default)
    {
        var (role, employee) = await Target(storeId, type, id, ct);
        return await Workspace(storeId, type, id, role, employee, ct);
    }

    private async Task<MenuVisibilityWorkspace> Workspace(int storeId, string type, int id, Role role, UserInStore? employee, CancellationToken ct)
    {
        var menus = await db.AdminMenuItems.AsNoTracking().Where(x => x.StoreId == storeId && !x.IsDeleted).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(ct);
        var permissions = role.RolePermissions.Select(x => x.Permission.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var isAdmin = role.Code.Equals("ADMIN", StringComparison.OrdinalIgnoreCase);
        var active = employee == null || (employee.IsActive && employee.User.IsActive);
        var nodes = menus.Select(x =>
        {
            var receiptAdminOnly = string.Equals(x.Controller, "ReceiptTemplates", StringComparison.OrdinalIgnoreCase) || (x.Url?.StartsWith("/admin/receipt-templates", StringComparison.OrdinalIgnoreCase) ?? false);
            var reason = !active ? "Nhân viên ngừng hoạt động" : !x.IsActive ? "Menu đã tắt toàn cửa hàng" :
                (receiptAdminOnly && !(isAdmin && role.IsSystemRole)) ? "Chỉ dành cho quản trị viên" :
                !string.IsNullOrWhiteSpace(x.PermissionCode) && !permissions.Contains(x.PermissionCode) ? "Nhóm chưa có quyền truy cập" : null;
            return new MenuVisibilityNode(x.Id, x.ParentId, x.Title, x.Icon ?? "bx bx-folder", reason == null, reason,
                string.IsNullOrWhiteSpace(x.Url) && string.IsNullOrWhiteSpace(x.Controller));
        }).ToList();
        nodes.InsertRange(0, new[] {
            new MenuVisibilityNode(MenuVisibilityRules.PosTerminals, null, "Quản lý máy POS", "bx bx-desktop", isAdmin && active, isAdmin ? null : "Chỉ dành cho quản trị viên", false),
            new MenuVisibilityNode(MenuVisibilityRules.Kiosks, null, "Quầy tự phục vụ", "bx bx-desktop", isAdmin && active, isAdmin ? null : "Chỉ dành cho quản trị viên", false)
        });
        var preferences = await db.Set<AdminMenuVisibilitySetting>().AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted).ToListAsync(ct);
        var roleJson = preferences.SingleOrDefault(x => x.RoleId == role.Id)?.HiddenMenuIdsJson;
        var personalIds = preferences.Where(x => x.UserInStoreId != null && x.HiddenMenuIdsJson != null).Select(x => x.UserInStoreId!.Value).ToHashSet();
        var memberIds = await db.UserInStores.Where(x => x.StoreId == storeId && x.RoleId == role.Id && !x.IsDeleted && !x.User.IsDeleted)
            .Select(x => x.Id).ToListAsync(ct);
        var members = memberIds.Select(x => personalIds.Contains(x)).ToList();
        var own = employee == null ? roleJson : preferences.SingleOrDefault(x => x.UserInStoreId == employee.Id)?.HiddenMenuIdsJson;
        var inherited = employee != null && own == null;
        var hidden = MenuVisibilityRules.Parse(own ?? roleJson).Order().ToList();
        var versionData = JsonSerializer.Serialize(new { storeId, type, id, RoleId = role.Id, RoleHidden = roleJson,
            Own = own, Nodes = nodes, Members = members.Count, Personal = members.Count(x => x) });
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(versionData)));
        return new(type, id, role.Id, employee == null ? role.Name : employee.User.FullName ?? employee.User.UserName,
            employee == null ? role.Code : role.Name, inherited, members.Count, members.Count(x => x), version,
            hidden, MenuVisibilityRules.Parse(roleJson).Order().ToList(), nodes);
    }

    public async Task<MenuVisibilityWorkspace> SaveAsync(int storeId, SaveMenuVisibilityRequest request, CancellationToken ct = default)
    {
        var (role, employee) = await Target(storeId, request.Type, request.Id, ct);
        // Track the original rowversion before checking the client's snapshot.
        var setting = await db.Set<AdminMenuVisibilitySetting>().SingleOrDefaultAsync(x => x.StoreId == storeId && !x.IsDeleted &&
            (employee == null ? x.RoleId == role.Id : x.UserInStoreId == employee.Id), ct);
        var current = await Workspace(storeId, request.Type, request.Id, role, employee, ct);
        if (!string.Equals(current.Version, request.Version, StringComparison.Ordinal))
            throw new InvalidOperationException("Cấu hình đã thay đổi ở nơi khác. Bấm Tải lại để xem bản mới trước khi lưu.");
        var ids = current.Menus.Select(x => x.Id).ToHashSet();
        if (request.HiddenIds == null || request.HiddenIds.Count > 2000 || request.HiddenIds.Any(x => !ids.Contains(x)))
            throw new ArgumentException("Danh sách menu không hợp lệ hoặc không thuộc cửa hàng này.");
        var json = request.Reset ? null : JsonSerializer.Serialize(request.HiddenIds.Distinct().Order().ToArray());
        if (setting == null)
        {
            setting = new AdminMenuVisibilitySetting { StoreId = storeId, RoleId = employee == null ? role.Id : null, UserInStoreId = employee?.Id };
            db.Add(setting);
        }
        setting.HiddenMenuIdsJson = json;
        // Role/UserInStore rowversions and their authentication stamps remain untouched.
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new InvalidOperationException("Có người vừa cập nhật cấu hình này. Hãy tải lại trước khi lưu."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 2601 or 2627)
        { throw new InvalidOperationException("Có người vừa tạo cấu hình này. Hãy tải lại trước khi lưu."); }
        return await Workspace(storeId, request.Type, request.Id, role, employee, ct);
    }

    public async Task<HashSet<int>> GetHiddenIdsAsync(int storeId, int userId, CancellationToken ct = default)
    {
        var member = await db.UserInStores.AsNoTracking().Where(x => x.StoreId == storeId && x.UserId == userId && !x.IsDeleted && x.IsActive && !x.User.IsDeleted && x.User.IsActive && x.Role.StoreId == storeId && !x.Role.IsDeleted)
            .Select(x => new { x.Id, x.RoleId }).SingleOrDefaultAsync(ct);
        if (member == null) return [];
        var settings = await db.Set<AdminMenuVisibilitySetting>().AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted && (x.RoleId == member.RoleId || x.UserInStoreId == member.Id)).ToListAsync(ct);
        var json = settings.SingleOrDefault(x => x.UserInStoreId == member.Id)?.HiddenMenuIdsJson
            ?? settings.SingleOrDefault(x => x.RoleId == member.RoleId)?.HiddenMenuIdsJson;
        return MenuVisibilityRules.Parse(json);
    }
}
