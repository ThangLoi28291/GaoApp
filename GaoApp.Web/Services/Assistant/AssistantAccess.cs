using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Infrastructure.Data;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Assistant;

public sealed class AssistantAccess(AppDbContext db, ICurrentUser currentUser, ITenantContext tenant, AssistantStoreSettings settings)
{
    private async Task<bool> CanMonitorAsync(int user,int store,CancellationToken ct)
    {
        var account=await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u=>u.Id==user&&u.IsActive&&!u.IsDeleted).Select(u=>new{u.IsHostAdmin}).SingleOrDefaultAsync(ct);
        if(account is null)return false;
        if(account.IsHostAdmin)return true;
        var codes=PermissionAliasMap.GetAcceptedCodes(PermissionCodes.System.Setting.Update);
        return await db.UserInStores.IgnoreQueryFilters().AnyAsync(m=>m.UserId==user&&m.StoreId==store&&m.IsActive&&!m.IsDeleted&&m.Role.StoreId==store&&!m.Role.IsDeleted
            &&m.Role.RolePermissions.Any(p=>codes.Contains(p.Permission.Code)),ct);
    }
    // Administration remains usable when store AI switches are off; this token has no chat/catalog rights.
    public async Task<AssistantIdentity> MonitorAsync(CancellationToken ct)
    {
        if(!currentUser.IsAuthenticated||currentUser.UserId is not >0||tenant.StoreId is not >0)throw AssistantException.Denied();
        var scope=new AssistantScope(tenant.StoreId.Value.ToString(),null,null);await ValidateScopeAsync(scope,ct);
        if(!await CanMonitorAsync(currentUser.UserId.Value,tenant.StoreId.Value,ct))throw AssistantException.Denied();
        return new(currentUser.UserId.Value,"Quản trị",scope,[AssistantPermissions.Use,AssistantPermissions.Monitor]);
    }
    public async Task<AssistantScreenContext> ScreenAsync(AssistantIdentity identity, string screen, int? documentId, CancellationToken ct)
    {
        if (screen == "product_create")
        {
            if (documentId is not null) throw AssistantException.Invalid();
            Require(identity.Permissions, AssistantPermissions.NormalizeName, AssistantPermissions.Catalog);
            return new(screen, "Tạo sản phẩm", "product_create", true, null);
        }
        if (screen != "receiving" || documentId is not > 0) throw AssistantException.Invalid();
        Require(identity.Permissions, AssistantPermissions.Receiving, AssistantPermissions.Catalog);
        var store = Id(identity.Scope.StoreId);
        var document = await db.StockDocuments.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.Id == documentId && d.StoreId == store && !d.IsDeleted && d.Type == StockDocumentType.Receipt)
            .Select(d => new { d.Id, d.DocumentNo, d.Status, d.RowVersion, d.WarehouseId, d.Warehouse.LegalEntityId }).SingleOrDefaultAsync(ct);
        if (document is null) throw new AssistantException(404, "document_not_found", "Không tìm thấy phiếu nhập được phép truy cập.");
        if (document.WarehouseId.ToString() != identity.Scope.WarehouseId || document.LegalEntityId.ToString() != identity.Scope.LegalEntityId)
            throw AssistantException.Denied();
        var editable = document.Status is StockDocumentStatus.Draft or StockDocumentStatus.Rejected;
        var version = Convert.ToBase64String(document.RowVersion);
        var number = new string(document.DocumentNo.Where(c => !char.IsControl(c)).Take(80).ToArray()).Trim();
        return new(screen, "Nhập hàng · " + number, $"receiving:{document.Id}:{(int)document.Status}:{version}", editable, document.Id,
            version, editable ? null : "Phiếu đã chuyển trạng thái. Mình không điền thêm hàng vào phiếu này.");
    }
    public async Task<string> CurrentStoreNameAsync(AssistantIdentity identity, CancellationToken ct)
    {
        // Display metadata follows the already-authorized server context, never a browser store ID.
        if (tenant.StoreId != Id(identity.Scope.StoreId) || currentUser.UserId != identity.UserId)
            throw AssistantException.Denied();
        var name = await db.Stores.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Id == tenant.StoreId && s.IsActive && !s.IsDeleted)
            .Select(s => s.Name).SingleOrDefaultAsync(ct);
        if (name is null) throw AssistantException.Denied();
        name = new string(name.Where(c => !char.IsControl(c)).Take(200).ToArray()).Trim();
        return name.Length > 0 ? name : $"Cửa hàng #{identity.Scope.StoreId}";
    }

    public async Task<string[]> PermissionsAsync(int userId, int storeId, CancellationToken ct)
    {
        var active = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive && !u.IsDeleted).Select(u => new { u.IsHostAdmin }).SingleOrDefaultAsync(ct);
        if (active is null || !await db.Stores.IgnoreQueryFilters().AnyAsync(s => s.Id == storeId && s.IsActive && !s.IsDeleted, ct))
            throw AssistantException.Denied();
        var switches = await settings.ReadAsync(storeId, ct);
        if (active.IsHostAdmin) return switches.Filter(AssistantPermissions.All);
        var codes = await db.UserInStores.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.UserId == userId && m.StoreId == storeId && m.IsActive && !m.IsDeleted
                && m.Role.StoreId == storeId && !m.Role.IsDeleted)
            .SelectMany(m => m.Role.RolePermissions.Select(p => p.Permission.Code))
            .Distinct().ToListAsync(ct);
        bool Has(string code) => codes.Any(c => PermissionAliasMap.GetAcceptedCodes(code).Contains(c, StringComparer.OrdinalIgnoreCase));
        var rights = AssistantPermissions.All.Where(p => p != AssistantPermissions.Catalog && Has(p)).ToHashSet(StringComparer.Ordinal);
        if (Has(PermissionCodes.Catalog.Product.View) || Has("inventory.stockdocument.create")) rights.Add(AssistantPermissions.Catalog);
        if (!Has("inventory.stockdocument.create")) rights.Remove(AssistantPermissions.Receiving);
        if (!Has(PermissionCodes.Catalog.Product.Create)) rights.Remove(AssistantPermissions.NormalizeName);
        return switches.Filter(rights);
    }
    public async Task ValidateScopeAsync(AssistantScope scope, CancellationToken ct)
    {
        var store = Id(scope.StoreId);
        if (!await db.Stores.IgnoreQueryFilters().AnyAsync(s => s.Id == store && s.IsActive && !s.IsDeleted, ct)) throw AssistantException.Denied();
        if (scope.LegalEntityId is { } legal && !await db.LegalEntities.IgnoreQueryFilters().AnyAsync(l => l.Id == Id(legal) && l.StoreId == store && l.IsActive && !l.IsDeleted, ct)) throw AssistantException.Denied();
        if (scope.WarehouseId is { } warehouse)
        {
            var w = await db.Warehouses.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(w => w.Id == Id(warehouse) && w.StoreId == store && w.IsActive && !w.IsDeleted, ct);
            if (w is null || scope.LegalEntityId is { } l && w.LegalEntityId != Id(l)) throw AssistantException.Denied();
        }
    }
    public async Task<AssistantIdentity> CurrentAsync(int? documentId, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not > 0 || tenant.StoreId is not > 0)
            throw new AssistantException(401, "unauthenticated", "Hãy đăng nhập lại để sử dụng trợ lý.");
        var store = tenant.StoreId.Value; var user = currentUser.UserId.Value;
        var rights = await PermissionsAsync(user, store, ct);
        Require(rights, AssistantPermissions.Use);
        string? warehouse = null, legal = null;
        if (documentId is { } id)
        {
            if (id <= 0) throw AssistantException.Invalid();
            Require(rights, AssistantPermissions.Receiving);
            var doc = await db.StockDocuments.AsNoTracking().Where(d => d.Id == id && d.StoreId == store && !d.IsDeleted)
                .Select(d => new { d.WarehouseId, d.Warehouse.LegalEntityId }).SingleOrDefaultAsync(ct);
            if (doc is null) throw new AssistantException(404, "document_not_found", "Không tìm thấy phiếu nhập được phép truy cập.");
            warehouse = doc.WarehouseId.ToString(); legal = doc.LegalEntityId.ToString();
        }
        var name = await db.Users.IgnoreQueryFilters().Where(u => u.Id == user && u.IsActive && !u.IsDeleted)
            .Select(u => u.FullName ?? u.UserName).SingleAsync(ct);
        name = new string(name.Where(c => !char.IsControl(c)).Take(120).ToArray()).Trim();
        if (name.Length == 0) name = "Bạn";
        var scope = new AssistantScope(store.ToString(), warehouse, legal);
        await ValidateScopeAsync(scope, ct);
        return new(user, name, scope, rights);
    }
    public async Task<AssistantIdentity> RecheckAsync(AssistantIdentity signed, CancellationToken ct)
    {
        await ValidateScopeAsync(signed.Scope, ct);
        if(signed.Permissions.Contains(AssistantPermissions.Monitor))
        {
            if(signed.Scope.WarehouseId is not null||signed.Scope.LegalEntityId is not null||!await CanMonitorAsync(signed.UserId,Id(signed.Scope.StoreId),ct))throw AssistantException.Denied();
            return signed with {Permissions=new[]{AssistantPermissions.Use,AssistantPermissions.Monitor}.Intersect(signed.Permissions,StringComparer.Ordinal).ToArray()};
        }
        var current = await PermissionsAsync(signed.UserId, Id(signed.Scope.StoreId), ct);
        return signed with { Permissions = current.Intersect(signed.Permissions, StringComparer.Ordinal).ToArray() };
    }
    public static int Id(string value) => int.TryParse(value, System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out var id) && id > 0 && id.ToString() == value ? id : throw AssistantException.Invalid();
    public static void Require(IEnumerable<string> rights, params string[] required)
    { if (!required.All(rights.Contains)) throw AssistantException.Denied(); }
}
public sealed record AssistantScreenContext(string Kind, string Title, string Key, bool CanApply, int? DocumentId,
    string? DocumentVersion = null, string? Message = null);
