using System.Security.Claims;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.AdminMenus;
using GaoApp.Application.Interfaces.Services.AdminMenus;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

namespace GaoApp.Tests.Ui;

// Service doubles exercise request lifetime and presentation policy boundaries, without a running app/database.
public sealed class AdminNavigationProviderTests
{
    private static AdminMenuItemDto Item(int id, string path, int order = 0) => new()
    {
        Id = id, Title = $"Cấu hình {id}", Url = "~" + path, Icon = "bx bx-store", IsActive = true, SortOrder = order
    };
    private static ViewContext View(int userId = 99) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(userId > 0 ? new[] { new Claim("UserId", userId.ToString()) } : [], "test")),
            Request = { PathBase = "/shop", Path = "/admin" }
        },
        RouteData = new RouteData(), ActionDescriptor = new ActionDescriptor()
    };
    private static AdminNavigationProvider Provider(int? storeId, MenuSource menus, Grants grants, bool admin = false, PinSource? pins = null)
        => new(menus, new Tenant(storeId), new StoreAccess(admin), grants, new UrlFactory(), pins ?? new PinSource());

    [Theory]
    [InlineData(null, 99)]
    [InlineData(0, 99)]
    [InlineData(1, 0)]
    public async Task Missing_identity_returns_empty_navigation_and_never_reads_another_scope(int? storeId, int userId)
    {
        var menus = new MenuSource();
        var grants = new Grants();
        var provider = Provider(storeId, menus, grants);
        var view = View(userId);
        Assert.Empty((await provider.GetAsync(view)).Nodes);
        Assert.Empty(await provider.GetPinsAsync(view));
        Assert.Empty(menus.Calls);
        Assert.Equal(0, grants.Calls);
    }

    [Theory]
    [InlineData(false, false, false, false, "")]
    [InlineData(false, true, true, true, "1,2,6")]
    [InlineData(false, false, true, false, "2")]
    [InlineData(true, false, true, false, "2,3,4,5")]
    [InlineData(true, true, true, true, "1,2,3,4,5,6")]
    public async Task Configured_links_still_require_POS_shift_home_and_store_administrator_access(
        bool admin, bool orders, bool shifts, bool home, string expected)
    {
        var menus = new MenuSource();
        menus.Rows[(1, 99)] = [Item(1, "/admin/pos"), Item(2, "/admin/pos-shift"),
            Item(3, "/admin/pos-shift/handover-slips"), Item(4, "/admin/pos-shift/manager-dashboard"),
            Item(5, "/admin/receipt-templates"), Item(6, "/admin/home")];
        var grants = new Grants();
        if (orders) grants.Allowed.Add(PermissionCodes.Pos.Order.View);
        if (shifts) grants.Allowed.Add(PermissionCodes.Pos.Shift.View);
        if (home) grants.Allowed.Add(PermissionCodes.Admin.DashboardView);
        var result = await Provider(1, menus, grants, admin).GetAsync(View());
        Assert.Equal(expected, string.Join(",", result.Destinations.Select(item => item.Id)));
        Assert.All(result.Destinations, item => Assert.StartsWith("/shop/admin/", item.Href!));
        Assert.Equal(new[] { (1, 99) }, menus.Calls);
    }

    [Fact]
    public async Task Sidebar_shortcuts_and_pins_share_one_snapshot_and_do_not_fill_missing_pin_slots()
    {
        var menus = new MenuSource();
        menus.Rows[(1, 99)] = [Item(1, "/admin/products", 20), Item(2, "/admin/warehouse-receiving", 10), Item(3, "/admin/pos", 30), Item(4, "/admin/pos/dashboard", 40)];
        var pins = new PinSource();
        pins.Rows[(1, 99)] = [3, 2, 999, 1, 4];
        var provider = Provider(1, menus, new Grants(PermissionCodes.Pos.Order.View), pins: pins);
        var view = View();
        var first = await provider.GetAsync(view);
        Assert.Same(first, await provider.GetAsync(view));
        Assert.Equal(new[] { 3, 2, 1 }, (await provider.GetPinsAsync(view)).Select(item => item.Id));
        Assert.Equal(new[] { 3, 2, 1 }, (await provider.GetPinsAsync(view)).Select(item => item.Id));
        Assert.Single(menus.Calls);
        Assert.Single(pins.Calls);
        Assert.Equal(new[] { 2, 1, 3, 4 }, first.Destinations.Select(item => item.Id));
    }

    [Fact]
    public async Task New_requests_recheck_store_role_and_filtered_menu_instead_of_reusing_a_previous_snapshot()
    {
        var menus = new MenuSource();
        menus.Rows[(1, 99)] = [Item(1, "/admin/pos")];
        menus.Rows[(2, 99)] = [Item(200, "/admin/pos-shift")];
        var pins = new PinSource();
        pins.Rows[(1, 99)] = [1];
        pins.Rows[(2, 99)] = [1, 200]; // A stale/foreign ID must not be filled from the other tree.
        var grants = new Grants(PermissionCodes.Pos.Order.View, PermissionCodes.Pos.Shift.View);
        var storeOne = Provider(1, menus, grants, pins: pins);
        Assert.Equal(1, Assert.Single((await storeOne.GetAsync(View())).Destinations).Id);
        Assert.Equal(1, Assert.Single(await storeOne.GetPinsAsync(View())).Id);

        var storeTwo = Provider(2, menus, grants, pins: pins);
        Assert.Equal(200, Assert.Single((await storeTwo.GetAsync(View())).Destinations).Id);
        Assert.Equal(200, Assert.Single(await storeTwo.GetPinsAsync(View())).Id);
        grants.Allowed.Clear();
        Assert.Empty((await Provider(1, menus, grants).GetAsync(View())).Nodes);
        grants.Allowed.Add(PermissionCodes.Pos.Order.View);
        menus.Rows[(1, 99)] = []; // E.g. an ancestor was hidden, or membership moved to another role.
        Assert.Empty((await Provider(1, menus, grants).GetAsync(View())).Nodes);
        Assert.Equal(new[] { (1, 99), (2, 99), (1, 99), (1, 99) }, menus.Calls);
    }

    private sealed record Tenant(int? StoreId) : ITenantContext
    {
        public bool IsHostAdmin => false;
        public string? Subdomain => null;
    }
    private sealed class StoreAccess(bool administrator) : IStoreAdminAccess
    {
        public Task<bool> IsAdminAsync(CancellationToken ct = default) => Task.FromResult(administrator);
        public Task RequireAsync(CancellationToken ct = default) => administrator ? Task.CompletedTask : Task.FromException(new UnauthorizedAccessException());
    }
    private sealed class UrlFactory : IUrlHelperFactory
    {
        public IUrlHelper GetUrlHelper(ActionContext context) => new UrlHelper(context);
    }
    private sealed class Grants(params string[] allowed) : IAuthorizationService
    {
        public HashSet<string> Allowed { get; } = new(allowed, StringComparer.OrdinalIgnoreCase);
        public int Calls { get; private set; }
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            Calls++;
            return Task.FromResult(Allowed.Contains(policyName) ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        }
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());
    }
    private sealed class MenuSource : IAdminMenuService
    {
        public Dictionary<(int, int), List<AdminMenuItemDto>> Rows { get; } = [];
        public List<(int, int)> Calls { get; } = [];
        public Task<List<AdminMenuItemDto>> GetForRenderAsync(int storeId, int userId, CancellationToken ct = default)
        {
            Calls.Add((storeId, userId));
            return Task.FromResult(Rows.GetValueOrDefault((storeId, userId)) ?? []);
        }
        public Task<List<AdminMenuItemDto>> GetForAdminAsync(int storeId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminMenuItemDto?> GetByIdAsync(int storeId, int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CreateAsync(int storeId, int? actorUserId, SaveAdminMenuItemRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(int storeId, int? actorUserId, SaveAdminMenuItemRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(bool Success, string? ErrorMessage)> DeleteAsync(int storeId, int id, int? actorUserId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminMenuFormOptionsDto> GetFormOptionsAsync(int storeId, int? excludeMenuId = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class PinSource : IAdminMenuPinService
    {
        public Dictionary<(int, int), IReadOnlyList<int>> Rows { get; } = [];
        public List<(int, int)> Calls { get; } = [];
        public Task<IReadOnlyList<int>> GetConfiguredIdsAsync(int storeId, int userId, CancellationToken ct = default)
        {
            Calls.Add((storeId, userId));
            return Task.FromResult(Rows.GetValueOrDefault((storeId, userId)) ?? Array.Empty<int>());
        }
        public Task<AdminMenuPinWorkspace> GetWorkspaceAsync(int storeId, int userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminMenuPinPreview> PreviewAsync(int storeId, int userId, string type, int id, IReadOnlyList<int> menuIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminMenuPinWorkspace> SaveAsync(int storeId, int userId, SaveAdminMenuPinsRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
