using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.AdminMenus;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Web.Areas.Admin.ViewModels.Commerce;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GaoApp.Web.Services;

/// <summary>Request-scoped presentation snapshot shared by the sidebar and shortcuts. No menu writes or defaults.</summary>
public sealed class AdminNavigationProvider(
    IAdminMenuService menus, ITenantContext tenant, IStoreAdminAccess storeAdmin,
    IAuthorizationService authorization, IUrlHelperFactory urlHelpers, IAdminMenuPinService pins)
{
    private Task<AdminNavigationSnapshot>? snapshot;
    private Task<IReadOnlyList<AdminNavigationNode>>? pinned;
    private Task<IReadOnlyList<MobilePrimaryTab>>? mobileDefaults;

    public Task<AdminNavigationSnapshot> GetAsync(ViewContext view) => snapshot ??= LoadAsync(view);

    public Task<IReadOnlyList<AdminNavigationNode>> GetPinsAsync(ViewContext view) => pinned ??= LoadPinsAsync(view);

    public Task<IReadOnlyList<MobilePrimaryTab>> GetMobileDefaultsAsync(ViewContext view)
        => mobileDefaults ??= LoadMobileDefaultsAsync(view);

    private async Task<IReadOnlyList<MobilePrimaryTab>> LoadMobileDefaultsAsync(ViewContext view)
    {
        var data = await GetAsync(view);
        if (data.StoreId <= 0 || data.UserId <= 0) return [];
        var canReceive = (await authorization.AuthorizeAsync(view.HttpContext.User, null,
            AppPermissions.InventoryStockDocumentCreate)).Succeeded;
        return MobilePrimaryNavigation.Select(data, view.HttpContext.Request.Path.Value ?? "", canReceive);
    }

    private async Task<AdminNavigationSnapshot> LoadAsync(ViewContext view)
    {
        var context = view.HttpContext;
        var ct = context.RequestAborted;
        int ClaimId(string type) => int.TryParse(context.User.FindFirst(type)?.Value, out var id) ? id : 0;
        var userId = new[] { "UserId", "userId", "NameIdentifier", System.Security.Claims.ClaimTypes.NameIdentifier }
            .Select(ClaimId).FirstOrDefault(id => id > 0);
        var storeId = tenant.StoreId ?? 0;
        var pathBase = context.Request.PathBase.Value ?? "";
        if (storeId <= 0 || userId <= 0)
            return new(storeId, userId, false, false, pathBase, Array.Empty<AdminNavigationNode>());

        // Sequential: these services may share the request's DbContext.
        var isAdmin = await storeAdmin.IsAdminAsync(ct);
        var canManage = (await authorization.AuthorizeAsync(context.User, null, PermissionCodes.Security.Role.Permissions)).Succeeded;
        var eligible = await menus.GetForRenderAsync(storeId, userId, ct);
        var url = urlHelpers.GetUrlHelper(view);
        string? Href(GaoApp.Application.DTOs.AdminMenus.AdminMenuItemDto item) => !string.IsNullOrWhiteSpace(item.Url)
            ? url.Content(item.Url)
            : url.Action(string.IsNullOrWhiteSpace(item.Action) ? "Index" : item.Action, item.Controller,
                new { area = string.IsNullOrWhiteSpace(item.Area) ? "Admin" : item.Area });
        var location = new AdminNavigationLocation(
            view.RouteData.Values["area"]?.ToString() ?? "", view.RouteData.Values["controller"]?.ToString() ?? "",
            view.RouteData.Values["action"]?.ToString() ?? "", context.Request.PathBase.Add(context.Request.Path).Value ?? "",
            context.Request.QueryString.Value ?? "");
        var tree = AdminNavigation.Build(eligible, Href, location, isAdmin);
        var canViewOrders = (await authorization.AuthorizeAsync(context.User, null, PermissionCodes.Pos.Order.View)).Succeeded;
        var canViewShifts = (await authorization.AuthorizeAsync(context.User, null, PermissionCodes.Pos.Shift.View)).Succeeded;
        var canViewHome = (await authorization.AuthorizeAsync(context.User, null, PermissionCodes.Admin.DashboardView)).Succeeded;
        var result = new AdminNavigationSnapshot(storeId, userId, isAdmin, canManage, pathBase, tree);
        // Retain controller policies even when a menu's configured PermissionCode is empty/mistaken.
        tree = result.Select(node =>
        {
            var path = result.LocalPath(node);
            if (AdminNavigationSnapshot.IsHomePath(path)) return canViewHome;
            if (AdminNavigationSnapshot.Under(path, "/admin/receipt-templates")) return isAdmin;
            if (AdminNavigationSnapshot.IsShiftPath(path))
                return canViewShifts && (isAdmin || !(AdminNavigationSnapshot.Under(path, "/admin/pos-shift/handover-slips")
                    || AdminNavigationSnapshot.Under(path, "/admin/pos-shift/manager-dashboard")));
            return !AdminNavigationSnapshot.Under(path, "/admin/pos") || canViewOrders;
        });
        return result with { Nodes = tree };
    }

    private async Task<IReadOnlyList<AdminNavigationNode>> LoadPinsAsync(ViewContext view)
    {
        var data = await GetAsync(view);
        if (data.StoreId <= 0 || data.UserId <= 0) return Array.Empty<AdminNavigationNode>();
        var ids = await pins.GetConfiguredIdsAsync(data.StoreId, data.UserId, view.HttpContext.RequestAborted);
        var destinations = data.Destinations.ToDictionary(node => node.Id);
        return ids.Distinct().Take(4).Where(destinations.ContainsKey).Select(id => destinations[id]).ToArray();
    }
}

public sealed record AdminNavigationSnapshot(int StoreId, int UserId, bool IsStoreAdmin, bool CanManageMenus,
    string PathBase, IReadOnlyList<AdminNavigationNode> Nodes)
{
    public IEnumerable<AdminNavigationNode> Destinations => Flatten(Nodes);

    public IReadOnlyList<AdminNavigationNode> PosNodes(bool includeReceiptTemplates = false) => Select(node =>
    {
        var path = LocalPath(node);
        return Under(path, "/admin/pos") || IsShiftPath(path)
            || (includeReceiptTemplates && Under(path, "/admin/receipt-templates"));
    });

    // Desktop POS's ellipsis is a compact set of existing store destinations, not a second sidebar.
    public IReadOnlyList<AdminNavigationNode> PosHeaderNodes()
    {
        string Kind(AdminNavigationNode node) => LocalPath(node).ToLowerInvariant() switch
        {
            "/admin/receipt-templates" => "printing",
            "/admin/pos/printer-settings" => "printer-settings",
            "/admin/pos-shift" => "shift",
            "/admin/pos-shift/history" or "/admin/pos-shift-history" => "history",
            "/admin/pos/orders-page" => "orders",
            "/admin/pos/dashboard" => "dashboard",
            _ => ""
        };
        return Destinations.Where(node => Kind(node) != "").DistinctBy(Kind).ToArray();
    }

    public AdminNavigationNode? Home => Destinations.FirstOrDefault(node => IsHomePath(LocalPath(node)));
    public bool IsCustomerDisplay(AdminNavigationNode node) => LocalPath(node).Equals("/admin/pos/customer-display", StringComparison.OrdinalIgnoreCase);

    public string LocalPath(AdminNavigationNode node)
    {
        var href = node.Href;
        if (string.IsNullOrWhiteSpace(href) || !href.StartsWith('/') || href.StartsWith("//") || href.StartsWith("/\\")) return "";
        var path = href.Split('#', 2)[0].Split('?', 2)[0].TrimEnd('/');
        if (PathBase.Length > 0)
        {
            if (!path.StartsWith(PathBase + "/", StringComparison.OrdinalIgnoreCase)) return "";
            path = path[PathBase.Length..];
        }
        return path;
    }

    // Prefixes classify existing destinations; they are never emitted as links or fallback entries.
    public static bool Under(string path, string prefix) => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    public static bool IsShiftPath(string path) => Under(path, "/admin/pos-shift") || Under(path, "/admin/pos-shift-history")
        || Under(path, "/admin/cash-adjustments") || Under(path, "/admin/payment-adjustments");
    public static bool IsHomePath(string path) => new[] { "/admin", "/admin/home", "/admin/dashboard" }
        .Contains(path, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AdminNavigationNode> Select(Func<AdminNavigationNode, bool> predicate)
    {
        IReadOnlyList<AdminNavigationNode> Visit(IEnumerable<AdminNavigationNode> source) => source.Select(node =>
        {
            var children = Visit(node.Children);
            return children.Count > 0 ? node with { Children = children }
                : node.Href is not null && predicate(node) ? node : null;
        }).OfType<AdminNavigationNode>().ToArray();
        return Visit(Nodes);
    }

    public static IEnumerable<AdminNavigationNode> Flatten(IEnumerable<AdminNavigationNode> source)
    {
        foreach (var node in source)
        {
            if (node.Href is not null) yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }
}
