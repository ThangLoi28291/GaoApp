using GaoApp.Web.Areas.Admin.ViewModels.Commerce;
using GaoApp.Web.Services;

namespace GaoApp.Tests.Ui;

public sealed class MobilePrimaryNavigationTests
{
    private static AdminNavigationNode Link(int id, string href, string title = "Tên menu của cửa hàng")
        => new(id, title, "bx bx-package", href, false, []);
    private static AdminNavigationSnapshot Snapshot(params AdminNavigationNode[] nodes)
        => new(1, 9, false, false, "/shop", nodes);

    [Fact]
    public void Four_defaults_keep_real_nested_menu_destinations_and_leave_the_original_tree_unchanged()
    {
        var sales = Link(30, "/shop/admin/pos");
        var orders = Link(20, "/shop/admin/pos/orders-page?status=Completed");
        var receiving = Link(40, "/shop/admin/warehouse-receiving");
        var home = Link(10, "/shop/admin/home");
        var group = new AdminNavigationNode(1, "Chức năng cửa hàng", "bx bx-folder", null, false, [sales, receiving, orders]);
        var snapshot = Snapshot(group, home, Link(99, "/shop/admin/products"));
        var tabs = MobilePrimaryNavigation.Select(snapshot, "/admin/pos/order-detail/7", true);
        Assert.Equal(new[] { "Tổng quan", "Đơn hàng", "Nhập hàng", "Bán hàng" }, tabs.Select(tab => tab.Label));
        Assert.Equal(new[] { 10, 20, 40, 30 }, tabs.Select(tab => tab.Menu.Id));
        Assert.Same(orders, tabs[1].Menu);
        Assert.Equal("/shop/admin/pos/orders-page?status=Completed", tabs[1].Menu.Href);
        Assert.True(tabs[1].IsCurrent);Assert.Single(tabs, tab => tab.IsCurrent);
        Assert.Equal(new[] { 30, 40, 20 }, group.Children.Select(node => node.Id));
        Assert.All(tabs, tab => Assert.Equal("Tên menu của cửa hàng", tab.Menu.Title));
    }

    [Theory]
    [InlineData("/admin/home", "overview")]
    [InlineData("/admin/pos/orders-page?status=Completed", "orders")]
    [InlineData("/admin/pos/order-detail/7", "orders")]
    [InlineData("/admin/warehouse-receiving/17", "receiving")]
    [InlineData("/admin/pos", "sales")]
    [InlineData("/admin/pos/v3", "sales")]
    [InlineData("/admin/products", null)]
    public void Active_section_follows_nested_pages_without_changing_the_store_link(string path, string? key)
    {
        var result = MobilePrimaryNavigation.Select(Snapshot(Link(1, "/shop/admin/dashboard"),
            Link(2, "/shop/admin/pos/orders-page"), Link(3, "/shop/admin/warehouse-receiving"), Link(4, "/shop/admin/pos")), path, true);
        Assert.Equal(key, result.SingleOrDefault(tab => tab.IsCurrent)?.Key);
    }

    [Fact]
    public void Missing_permissions_routes_and_external_or_API_links_never_get_synthetic_replacements()
    {
        Assert.Empty(MobilePrimaryNavigation.Select(Snapshot(), "/admin/home", true));
        var snapshot = Snapshot(Link(1, "https://other.example/admin/home"), Link(2, "/other/admin/pos"),
            Link(3, "/shop/admin/pos/orders"), Link(4, "/shop/admin/warehouse-receiving/receipts"),
            Link(5, "/shop/admin/pos/customer-display"), Link(6, "/shop/admin/warehouse-receiving"));
        Assert.Empty(MobilePrimaryNavigation.Select(snapshot, "/admin/home", false));
        Assert.Equal(6, Assert.Single(MobilePrimaryNavigation.Select(snapshot, "/admin/warehouse-receiving", true)).Menu.Id);
    }

    [Fact]
    public void Sales_prefers_the_existing_canonical_menu_when_legacy_and_v3_are_also_present()
    {
        var result = MobilePrimaryNavigation.Select(Snapshot(Link(1, "/shop/admin/pos/legacy"),
            Link(2, "/shop/admin/pos/v3"), Link(3, "/shop/admin/pos")), "/admin/pos", false);
        Assert.Equal(3, Assert.Single(result).Menu.Id);
        Assert.Empty(MobilePrimaryNavigation.Select(Snapshot(Link(3, "/shop/admin/pos")) with { UserId = 0 }, "/admin/pos", true));
    }
}
