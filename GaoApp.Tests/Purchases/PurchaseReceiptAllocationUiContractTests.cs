namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptAllocationUiContractTests
{
    [Fact]
    public void Purchase_order_details_uses_server_projection_and_has_no_future_features()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin", "Views", "PurchaseOrders", "Details.cshtml"));
        Assert.Contains("Số đặt", view);
        Assert.Contains("Đã nhập", view);
        Assert.Contains("Đang xử lý", view);
        Assert.Contains("Còn có thể tạo phiếu", view);
        Assert.Contains("line.AvailableToAllocateQuantity", view);
        Assert.Contains("Toàn bộ số lượng còn lại đang nằm trong phiếu nhập chưa hoàn tất.", view);
        Assert.DoesNotContain("overdelivery", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alternate unit", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reopen", view, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
