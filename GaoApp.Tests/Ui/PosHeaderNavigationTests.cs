using GaoApp.Web.Areas.Admin.ViewModels.Commerce;
using GaoApp.Web.Services;

namespace GaoApp.Tests.Ui;

public sealed class PosHeaderNavigationTests
{
    private static AdminNavigationNode Link(int id, string path) => new(id, $"Menu cửa hàng {id}", "bx bx-receipt", path, false, []);
    [Fact]
    public void Desktop_header_keeps_only_original_shortcut_destinations_and_preserves_configured_metadata()
    {
        AdminNavigationNode[] source = [Link(1,"/admin/pos/return-to-stock"), Link(2,"/admin/pos-shift/handover-slips"),
            Link(3,"/admin/pos-shift/manager-dashboard"), Link(4,"/admin/pos"), Link(5,"/admin/pos/dashboard?terminalId=3"),
            Link(6,"/admin/pos/orders-page?status=completed"), Link(7,"/admin/pos-shift"), Link(8,"/admin/pos-shift/history"),
            Link(9,"/admin/receipt-templates"), Link(10,"/admin/pos/customer-display")];
        var snapshot = new AdminNavigationSnapshot(1,2,false,false,"",source);
        Assert.Equal(new[]{5,6,7,8,9},snapshot.PosHeaderNodes().Select(x=>x.Id));
        Assert.Same(source[4],snapshot.PosHeaderNodes()[0]);
        Assert.Equal(10,snapshot.Destinations.Count());
    }
    [Fact]
    public void Header_keeps_store_path_base_and_query_and_deduplicates_aliases_without_creating_missing_links()
    {
        var snapshot = new AdminNavigationSnapshot(1,2,false,false,"/shop",[
            Link(1,"/shop/admin/pos-shift-history?from=2026-10-01"),Link(2,"/shop/admin/pos-shift/history"),
            Link(3,"/admin/pos/orders-page"),Link(4,"https://outside.test/admin/pos/dashboard"),Link(5,"/shop/admin/pos/dashboard-extra")]);
        Assert.Equal(1,Assert.Single(snapshot.PosHeaderNodes()).Id);
        Assert.EndsWith("?from=2026-10-01",snapshot.PosHeaderNodes()[0].Href);
        Assert.Empty(new AdminNavigationSnapshot(1,2,false,false,"",[]).PosHeaderNodes());
    }
    [Fact]
    public void Printer_settings_remains_a_separate_configured_destination_from_admin_templates()
    {
        var printer=Link(12,"/shop/admin/pos/printer-settings");var templates=Link(13,"/shop/admin/receipt-templates");
        var snapshot=new AdminNavigationSnapshot(1,2,false,false,"/shop",[printer,templates]);
        Assert.Equal(new[]{12,13},snapshot.PosHeaderNodes().Select(x=>x.Id));
        Assert.Contains(printer,snapshot.PosNodes());
        Assert.DoesNotContain(templates,snapshot.PosNodes());
    }
}
