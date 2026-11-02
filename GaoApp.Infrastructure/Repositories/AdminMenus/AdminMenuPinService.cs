using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.AdminMenus;
using GaoApp.Application.Interfaces.Services.AdminMenus;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Services.AdminMenus;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.AdminMenus;

public sealed class AdminMenuPinService(AppDbContext db, ITenantContext tenant, IAdminMenuService menus,
    IAdminMenuVisibilityService visibility, ISecurityAdministrationGuard security,
    ICurrentStorePermissionService permissions) : IAdminMenuPinService
{
    private void RequireStore(int storeId)
    {
        if (storeId <= 0 || tenant.StoreId != storeId)
            throw new ForbiddenAppException("Ngữ cảnh cửa hàng không hợp lệ.");
    }

    private async Task<SecurityAdministrator> RequireEditor(int storeId, int userId, CancellationToken ct)
    {
        RequireStore(storeId);
        var actor = await security.GetActorAsync(storeId, userId, ct);
        if (!actor.CanAdminister || (!actor.IsHostAdmin && !await permissions.HasPermissionAsync(storeId, userId, PermissionCodes.Security.Role.Permissions, ct)))
            throw new ForbiddenAppException("Bạn không có quyền chỉnh mục ghim của cửa hàng.");
        return actor;
    }

    private Task<AdminMenuPinSetting?> Setting(int storeId, CancellationToken ct) => db.Set<AdminMenuPinSetting>()
        .AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == storeId && !x.IsDeleted, ct);

    public async Task<IReadOnlyList<int>> GetConfiguredIdsAsync(int storeId, int userId, CancellationToken ct = default)
    {
        RequireStore(storeId);
        var actor = await security.GetActorAsync(storeId, userId, ct);
        var configured = AdminMenuPinRules.Parse((await Setting(storeId, ct))?.PinnedMenuIdsJson);
        if (configured.Count == 0) return [];
        var eligible = AdminMenuPinRules.Choices(await menus.GetForRenderAsync(storeId, userId, ct), actor.CanAdminister)
            .Select(x => x.Id).ToHashSet();
        return configured.Where(eligible.Contains).ToArray();
    }

    private async Task<AdminMenuPinWorkspace> Workspace(int storeId, int userId, CancellationToken ct)
    {
        var setting = await Setting(storeId, ct);
        var tree = await menus.GetForRenderAsync(storeId, userId, ct);
        var choices = AdminMenuPinRules.Choices(tree, true);
        var versionData = JsonSerializer.Serialize(new { storeId, Id = setting?.Id, RowVersion = setting?.RowVersion, PinnedMenuIdsJson = setting?.PinnedMenuIdsJson, Tree = tree });
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(versionData)));
        return new(version, AdminMenuPinRules.Parse(setting?.PinnedMenuIdsJson), choices, await visibility.GetSubjectsAsync(storeId, ct));
    }

    public async Task<AdminMenuPinWorkspace> GetWorkspaceAsync(int storeId, int userId, CancellationToken ct = default)
    {
        await RequireEditor(storeId, userId, ct);
        return await Workspace(storeId, userId, ct);
    }

    public async Task<AdminMenuPinPreview> PreviewAsync(int storeId, int userId, string type, int id, IReadOnlyList<int> menuIds, CancellationToken ct = default)
    {
        await RequireEditor(storeId, userId, ct);
        var choices = AdminMenuPinRules.Choices(await menus.GetForRenderAsync(storeId, userId, ct), true);
        AdminMenuPinRules.Validate(menuIds, choices.Select(x => x.Id));
        if (string.IsNullOrEmpty(type)) return new(menuIds.ToArray());
        if (type is not ("role" or "employee") || id <= 0) throw new ArgumentException("Đối tượng xem trước không hợp lệ.");
        var target = await visibility.GetAsync(storeId, type, id, ct);
        var visible = MenuVisibilityRules.VisibleIds(target.Menus, target.HiddenIds.ToHashSet());
        var branches = target.Menus.Where(x => x.ParentId.HasValue && visible.Contains(x.Id))
            .Select(x => x.ParentId!.Value).ToHashSet();
        return new(menuIds.Where(x => visible.Contains(x) && !branches.Contains(x)).ToArray());
    }

    public async Task<AdminMenuPinWorkspace> SaveAsync(int storeId, int userId, SaveAdminMenuPinsRequest request, CancellationToken ct = default)
    {
        RequireStore(storeId);
        await using var scope = await security.BeginAsync(storeId, userId, PermissionCodes.Security.Role.Permissions, ct);
        scope.RequireAdministrator();
        var setting = await db.Set<AdminMenuPinSetting>().SingleOrDefaultAsync(x => x.StoreId == storeId && !x.IsDeleted, ct);
        var current = await Workspace(storeId, userId, ct);
        if (!string.Equals(request.Version, current.Version, StringComparison.Ordinal))
            throw new InvalidOperationException("Cấu hình hoặc menu đã đổi. Bấm Tải lại rồi kiểm tra trước khi lưu.");
        AdminMenuPinRules.Validate(request.MenuIds, current.Choices.Select(x => x.Id));
        var before = current.MenuIds.ToArray();
        if (setting == null)
        {
            setting = new AdminMenuPinSetting { StoreId = storeId };
            db.Add(setting);
        }
        setting.PinnedMenuIdsJson = JsonSerializer.Serialize(request.MenuIds);
        if (setting.Id > 0) db.Entry(setting).Property(x => x.PinnedMenuIdsJson).IsModified = true;
        try
        {
            await scope.CompleteAsync("Cập nhật mục ghim điện thoại", nameof(AdminMenuPinSetting), setting.Id,
                new { MenuIds = before }, new { request.MenuIds }, ct: ct);
        }
        catch (DbUpdateConcurrencyException) { throw new InvalidOperationException("Có người vừa sửa mục ghim. Hãy tải lại trước khi lưu."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new InvalidOperationException("Có người vừa lưu mục ghim. Hãy tải lại trước khi lưu."); }
        return await Workspace(storeId, userId, ct);
    }
}
