using GaoApp.Web.Areas.Admin.ViewModels.Commerce;

namespace GaoApp.Web.Services;

public sealed record MobilePrimaryTab(string Key, string Label, AdminNavigationNode Menu, bool IsCurrent);

/// <summary>Phone defaults reference the permission-filtered store menu; never create destinations or settings.</summary>
public static class MobilePrimaryNavigation
{
    public static IReadOnlyList<MobilePrimaryTab> Select(AdminNavigationSnapshot navigation, string currentPath, bool canReceive)
    {
        if (navigation.StoreId <= 0 || navigation.UserId <= 0) return [];
        var destinations = navigation.Destinations.ToArray();
        var current = Section(currentPath);
        var result = new List<MobilePrimaryTab>();
        void Add(string key, string label, params string[] paths)
        {
            var node = paths.Select(path => destinations.FirstOrDefault(menu =>
                string.Equals(navigation.LocalPath(menu), path, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(menu => menu is not null);
            if (node is not null) result.Add(new(key, label, node, current == key));
        }
        Add("overview", "Tổng quan", "/admin", "/admin/home", "/admin/dashboard");
        // orders is a JSON endpoint; orders-page is the actual order-list view.
        Add("orders", "Đơn hàng", "/admin/pos/orders-page");
        if (canReceive) Add("receiving", "Nhập hàng", "/admin/warehouse-receiving");
        Add("sales", "Bán hàng", "/admin/pos", "/admin/pos/v3", "/admin/pos/legacy");
        return result;
    }

    private static string? Section(string path)
    {
        path = path.Split(['?', '#'], 2)[0].TrimEnd('/');
        if (AdminNavigationSnapshot.IsHomePath(path)) return "overview";
        if (AdminNavigationSnapshot.Under(path, "/admin/pos/orders-page") ||
            AdminNavigationSnapshot.Under(path, "/admin/pos/order-detail")) return "orders";
        if (AdminNavigationSnapshot.Under(path, "/admin/warehouse-receiving")) return "receiving";
        return AdminNavigationSnapshot.Under(path, "/admin/pos") ? "sales" : null;
    }
}
