using System.Text.Json;
using GaoApp.Application.DTOs.AdminMenus;
using GaoApp.Web.Areas.Admin.ViewModels.Commerce;

public static class NavigationContract
{
    public static void Check()
    {
        var assertions = 0;
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); assertions++; }
        AdminNavigationLocation location = new("Admin", "Orders", "Index", "/store/Admin/Orders", "?tab=open&page=2");
        IReadOnlyList<AdminNavigationNode> Build(IEnumerable<AdminMenuItemDto> source, bool admin = false)
            => AdminNavigation.Build(source, x => x.Url ?? "/Admin/" + x.Controller + "/" + (x.Action ?? "Index"), location, admin);
        var leaf = NavigationFixture.Item(3, "Đơn hàng", "/store/Admin/Orders?tab=open");
        var root = NavigationFixture.Folder(1, "Nhóm ngoài", NavigationFixture.Folder(2, "Nhóm trong", leaf));
        var before = JsonSerializer.Serialize(root);
        var deep = Build([root]);
        Require(deep[0].Children[0].Children[0].IsCurrent, "URL path-base/query active match lost.");
        Require(deep[0].ContainsCurrent && deep[0].Children[0].ContainsCurrent, "Active ancestry lost.");
        Require(JsonSerializer.Serialize(root) == before, "Source tree was mutated.");
        leaf.Url = "/store/Admin/Orders?tab=closed";
        Require(!Build([root])[0].ContainsCurrent, "Different configured query incorrectly selected.");
        leaf.Url = "/store/Admin/Orders-extra";
        Require(!Build([root])[0].ContainsCurrent, "Path prefix incorrectly selected.");
        leaf.Url = "https://example.invalid/store/Admin/Orders";
        Require(!Build([root])[0].ContainsCurrent, "External destination incorrectly selected.");
        leaf.Url = null; leaf.Controller = "Orders"; leaf.Action = "Index";
        Require(Build([root])[0].ContainsCurrent, "Controller/action active match lost.");
        leaf.Area = "Other";
        Require(!Build([root])[0].ContainsCurrent, "Different area incorrectly selected.");
        leaf.IsActive = false;
        Require(Build([root]).Count == 0, "Inactive leaf or empty ancestors retained.");
        Require(Build([]).Count == 0, "Empty configuration gained a destination.");
        var receipt = NavigationFixture.Item(8, "Mẫu phiếu", "/admin/receipt-templates");
        Require(Build([NavigationFixture.Folder(7, "Nhóm", receipt)]).Count == 0, "Receipt administrator rule lost at depth.");
        Require(Build([receipt], true).Count == 1, "Administrator receipt entry lost.");
        var a = NavigationFixture.Item(20, "A", "/a"); var b = NavigationFixture.Item(10, "B", "/b");
        Require(Build([a, b]).Select(x => x.Id).SequenceEqual([10, 20]), "Configured order lost.");
        Require(Build([b]).Single().Id == 10, "Filtered input gained entries.");
        Require(Build([a, a]).Count == 1, "Duplicate IDs produced duplicate DOM IDs.");
        var cycle = NavigationFixture.Folder(100, "Cycle"); cycle.Children.Add(cycle);
        Require(Build([cycle]).Count == 0, "Malformed cycle did not terminate safely.");
        Console.WriteLine($"PASS: {assertions} navigation projection checks; hierarchy, active route, order, filtered/empty input, receipt restriction and source immutability.");
    }
}
