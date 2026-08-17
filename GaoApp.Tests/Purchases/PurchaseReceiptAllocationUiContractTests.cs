namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptAllocationUiContractTests
{
    [Fact]
    public void Purchase_order_details_separates_planned_availability_from_managed_overdelivery()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin", "Views", "PurchaseOrders", "Details.cshtml"));
        Assert.Contains("Số đặt", view);
        Assert.Contains("Đã nhập", view);
        Assert.Contains("Đang xử lý", view);
        Assert.Contains("Còn theo kế hoạch", view);
        Assert.Contains("line.AvailableToAllocateQuantity", view);
        Assert.Contains("line.ConfirmedOverdeliveryQuantity", view);
        Assert.Contains("line.ProjectedOverdeliveryQuantity", view);
        Assert.Contains("MaximumEntryQuantity", view);
        var script = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js", "purchase-order-details.js"));
        Assert.Contains("querySelectorAll('.receipt-input')", script);
        Assert.Contains("input.disabled = !checkbox.checked", script);
        Assert.Contains("Toàn bộ số lượng còn lại đang nằm trong phiếu nhập chưa hoàn tất.", view);
        Assert.Contains("projectedOverdelivery", script);
        Assert.DoesNotContain("alternate unit", view, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
