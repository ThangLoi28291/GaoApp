using GaoApp.Application.DTOs.AdminMenus;
using GaoApp.Application.Services.AdminMenus;
using GaoApp.Web.Areas.Admin.ViewModels.Commerce;
using GaoApp.Web.Services;

namespace GaoApp.Tests.Ui;

// Pure projection tests: no web host, SQL Server, menu seeding or application startup.
public sealed class AdminNavigationEdgeCaseTests
{
    private static readonly AdminNavigationLocation Location = new("Admin", "Product", "Index", "/admin/products", "");
    private static AdminMenuItemDto Link(int id, string url, int order = 0) => new()
    {
        Id = id, Title = $"Mục {id}", Icon = "bx bx-package", Url = url, IsActive = true, SortOrder = order
    };
    private static IReadOnlyList<AdminNavigationNode> Build(IEnumerable<AdminMenuItemDto> source,
        AdminNavigationLocation? location = null, bool administrator = false)
        => AdminNavigation.Build(source, item => item.Url, location ?? Location, administrator);

    [Fact]
    public void Empty_disabled_and_unresolvable_menus_never_get_default_destinations()
    {
        Assert.Empty(Build([]));
        var disabled = Link(1, "/admin/products");
        disabled.IsActive = false;
        disabled.Children.Add(Link(2, "/admin/products"));
        Assert.Empty(Build([disabled, new() { Id = 3, Title = "Nhóm trống", IsActive = true }]));
        Assert.Empty(AdminNavigation.Build([Link(4, "/configured")], _ => null, Location, false));
    }

    [Fact]
    public void Single_item_keeps_its_full_unicode_label_icon_and_configured_destination()
    {
        var source = Link(9, "/admin/products?view=compact#list");
        source.Title = string.Concat(Enumerable.Repeat("Nhập hàng · Cửa hàng số 01 🛒 ", 30));
        var node = Assert.Single(Build([source]));
        Assert.Equal(source.Id, node.Id);
        Assert.Equal(source.Title, node.Title);
        Assert.Equal(source.Icon, node.Icon);
        Assert.Equal(source.Url, node.Href);
        Assert.Empty(node.Children);
    }

    [Fact]
    public void Deep_and_wide_trees_keep_all_eligible_items_in_configured_order_without_mutation()
    {
        var root = new AdminMenuItemDto { Id = 1, Title = "Kho", IsActive = true };
        var parent = root;
        for (var id = 2; id <= 64; id++)
        {
            var child = new AdminMenuItemDto { Id = id, Title = $"Nhóm {id}", IsActive = true };
            parent.Children.Add(child);
            parent = child;
        }
        parent.Children.Add(Link(65, "/admin/products"));
        var siblings = Enumerable.Range(100, 200).Reverse().Select(id => Link(id, $"/configured/{id}", id)).ToArray();
        var source = new[] { root }.Concat(siblings).ToArray();
        var result = Build(source);
        var leaves = AdminNavigationSnapshot.Flatten(result).ToArray();
        Assert.Equal(new[] { 65 }.Concat(Enumerable.Range(100, 200)), leaves.Select(item => item.Id));
        Assert.True(result[0].ContainsCurrent);
        Assert.Equal(Enumerable.Range(100, 200).Reverse(), siblings.Select(item => item.Id));
        Assert.Equal(65, Assert.Single(parent.Children).Id);
    }

    [Fact]
    public void Cycles_and_duplicate_ids_do_not_create_repeated_or_synthetic_links()
    {
        var cycle = new AdminMenuItemDto { Id = 1, Title = "Vòng lặp", IsActive = true };
        cycle.Children.Add(cycle);
        var first = Link(2, "/configured/first");
        var result = Build([cycle, first, Link(2, "/configured/duplicate")]);
        Assert.Equal("/configured/first", Assert.Single(result).Href);
        Assert.Same(cycle, Assert.Single(cycle.Children));
    }

    [Theory]
    [InlineData("/shop/admin/pos-shift/requests", "?tab=cash&page=2", true)]
    [InlineData("/shop/admin/pos-shift/requests/", "?tab=cash", true)]
    [InlineData("/shop/admin/pos-shift/requests", "?tab=payment", false)]
    [InlineData("/shop/admin/pos-shift/requests-more", "?tab=cash", false)]
    [InlineData("/other/admin/pos-shift/requests", "?tab=cash", false)]
    public void Current_item_respects_path_base_exact_path_and_required_query(string path, string query, bool expected)
    {
        var source = Link(1, "/shop/admin/pos-shift/requests?tab=cash#requests");
        var location = new AdminNavigationLocation("Admin", "POSRequests", "Index", path, query);
        Assert.Equal(expected, Assert.Single(Build([source], location)).IsCurrent);
    }

    [Fact]
    public void Hidden_or_unavailable_ancestors_remove_descendants_and_empty_containers()
    {
        MenuVisibilityNode[] source = [
            new(1, null, "Nhóm", "", true, null, true),
            new(2, 1, "Chức năng", "", true, null, false),
            new(3, null, "Nhóm thiếu quyền", "", false, "denied", true),
            new(4, 3, "Con", "", true, null, false),
            new(5, null, "Nhóm trống", "", true, null, true),
            new(6, 99, "Không còn cha", "", true, null, false)
        ];
        Assert.Equal(new[] { 1, 2 }, MenuVisibilityRules.VisibleIds(source, new HashSet<int>()).Order());
        Assert.Empty(MenuVisibilityRules.VisibleIds(source, new HashSet<int> { 1 }));
        Assert.Empty(MenuVisibilityRules.VisibleIds(source, new HashSet<int> { 2 }));
    }

    [Fact]
    public void Pos_projection_preserves_real_groups_and_segment_boundaries_without_mutating_sidebar()
    {
        var folder = new AdminMenuItemDto { Id = 1, Title = "Vận hành", IsActive = true, Children = [
            Link(2, "/shop/admin/pos-shift/history", 20), Link(3, "/shop/admin/products", 10),
            Link(4, "/shop/admin/pos", 30), Link(5, "/shop/admin/position", 40)] };
        var nodes = Build([folder]);
        var snapshot = new AdminNavigationSnapshot(1, 99, false, false, "/shop", nodes);
        var selected = Assert.Single(snapshot.PosNodes());
        Assert.Equal(folder.Id, selected.Id);
        Assert.Equal(folder.Title, selected.Title);
        Assert.Equal(new[] { 2, 4 }, selected.Children.Select(item => item.Id));
        Assert.Equal(new[] { 3, 2, 4, 5 }, Assert.Single(nodes).Children.Select(item => item.Id));
        Assert.Empty(snapshot.Select(_ => false));
        Assert.Equal(4, Assert.Single(nodes).Children.Count);
    }

    [Fact]
    public void Receipt_administrator_rule_removes_the_whole_nested_branch()
    {
        var receipts = Link(2, "/admin/receipt-templates");
        receipts.Children.Add(Link(3, "/admin/receipt-templates/preview"));
        var folder = new AdminMenuItemDto { Id = 1, Title = "Quản trị", IsActive = true, Children = [receipts] };
        Assert.Empty(Build([folder]));
        Assert.Equal(3, Assert.Single(AdminNavigationSnapshot.Flatten(Build([folder], administrator: true))).Id);
    }
}
