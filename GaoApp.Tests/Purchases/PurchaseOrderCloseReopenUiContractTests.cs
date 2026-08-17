namespace GaoApp.Tests.Purchases;

public sealed class PurchaseOrderCloseReopenUiContractTests
{
    [Fact]
    public void Close_and_reopen_endpoints_require_purchase_order_close_permission()
    {
        var controller = Read("GaoApp.Web", "Areas", "Admin", "Controllers", "PurchaseOrdersController.cs");

        Assert.Contains("CloseOutstandingLine", controller);
        Assert.Contains("CloseAllOutstanding", controller);
        Assert.Contains("ReopenOutstandingLine", controller);
        Assert.Contains("ReopenAllOutstanding", controller);
        Assert.True(Count(controller,
            "[Authorize(Policy = PermissionCodes.Purchase.Order.Close)]") >= 4);
    }

    [Fact]
    public void Detail_view_exposes_line_and_whole_order_controls_with_server_row_version()
    {
        var view = Read("GaoApp.Web", "Areas", "Admin", "Views", "PurchaseOrders", "Details.cshtml");

        Assert.Contains("asp-action=\"CloseOutstandingLine\"", view);
        Assert.Contains("asp-action=\"CloseAllOutstanding\"", view);
        Assert.Contains("asp-action=\"ReopenOutstandingLine\"", view);
        Assert.Contains("asp-action=\"ReopenAllOutstanding\"", view);
        Assert.Contains("name=\"RowVersion\" value=\"@order.RowVersion\"", view);
        Assert.Contains("name=\"Reason\" rows=\"4\" maxlength=\"500\" required", view);
        Assert.Contains("placeholder=\"Không bắt buộc\"", view);
    }

    [Fact]
    public void Service_acquires_parent_lock_before_loading_or_mutating_order()
    {
        var service = Read("GaoApp.Application", "Services", "Purchases", "PurchaseOrderService.cs");
        var repository = Read("GaoApp.Infrastructure", "Repositories", "Purchases", "PurchaseOrderRepository.cs");
        var method = service[service.IndexOf("private async Task ManageOutstandingAsync", StringComparison.Ordinal)..];

        Assert.True(method.IndexOf("LockForOutstandingManagementAsync", StringComparison.Ordinal) <
                    method.IndexOf("GetDetailAsync", StringComparison.Ordinal));
        Assert.Contains("HasActiveReceiptLinesAsync", method);
        Assert.Contains("UPDLOCK,HOLDLOCK,ROWLOCK", repository);
        Assert.Contains("CurrentTransaction", repository);
    }

    private static int Count(string value, string fragment)
        => value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static string Read(params string[] segments)
        => File.ReadAllText(Path.Combine(new[] { FindRepositoryRoot() }.Concat(segments).ToArray()));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
