using System.Text.RegularExpressions;

namespace GaoApp.Tests.Ui;

public sealed class CustomerManagementModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ViewRoot = Path.Combine(RepositoryRoot, "GaoApp.Web", "Areas", "Admin", "Views", "Customer");
    private static readonly string ControllerPath = Path.Combine(RepositoryRoot, "GaoApp.Web", "Areas", "Admin", "Controllers", "CustomerController.cs");
    private static readonly string ScriptPath = Path.Combine(RepositoryRoot, "GaoApp.Web", "wwwroot", "Admin", "js", "customer.js");
    private static readonly string StylePath = Path.Combine(RepositoryRoot, "GaoApp.Web", "wwwroot", "Admin", "css", "customer-index.css");

    [Fact]
    public void Index_should_expose_exact_kpis_filters_desktop_mobile_and_no_internal_id()
    {
        var index = File.ReadAllText(Path.Combine(ViewRoot, "Index.cshtml"));
        var table = File.ReadAllText(Path.Combine(ViewRoot, "_CustomerTable.cshtml"));

        Assert.Contains("data-customer-shell", index, StringComparison.Ordinal);
        Assert.Contains("customerTotalCount", index, StringComparison.Ordinal);
        Assert.Contains("customerActiveCount", index, StringComparison.Ordinal);
        Assert.Contains("customerInactiveCount", index, StringComparison.Ordinal);
        Assert.Contains("customerDebtEnabledCount", index, StringComparison.Ordinal);
        Assert.Contains("id=\"txtSearch\"", index, StringComparison.Ordinal);
        Assert.Contains("id=\"ddlPriceTier\"", index, StringComparison.Ordinal);
        Assert.Contains("id=\"ddlStatus\"", index, StringComparison.Ordinal);
        Assert.Contains("id=\"ddlDebt\"", index, StringComparison.Ordinal);
        Assert.Contains("id=\"ddlPageSize\"", index, StringComparison.Ordinal);
        Assert.Contains("data-customer-reward-vouchers-link", index, StringComparison.Ordinal);
        Assert.Contains("customer-desktop-list", table, StringComparison.Ordinal);
        Assert.Contains("customer-mobile-list", table, StringComparison.Ordinal);
        Assert.DoesNotContain(">ID<", table, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_edit_should_share_one_bounded_form_and_hide_import_reward_fields()
    {
        var edit = File.ReadAllText(Path.Combine(ViewRoot, "Edit.cshtml"));

        Assert.Contains("asp-for=\"Name\"", edit, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"Code\"", edit, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"Phone\"", edit, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"Email\"", edit, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"TaxCode\"", edit, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"PriceTier\"", edit, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"HaveDebt\"", edit, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"IsActive\"", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("ImportedRewardAmount", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("OldCustomerId", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomerGroup", edit, StringComparison.Ordinal);
    }

    [Fact]
    public void Interaction_should_be_realtime_latest_request_safe_and_confirmation_styled()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("customerRequestSequence", script, StringComparison.Ordinal);
        Assert.Contains("customerSearchTimer", script, StringComparison.Ordinal);
        Assert.Contains("350", script, StringComparison.Ordinal);
        Assert.Contains("Swal.fire", script, StringComparison.Ordinal);
        Assert.Contains("data-customer-quick-view", script, StringComparison.Ordinal);
        Assert.DoesNotContain("window.alert", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("window.confirm", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Controller_should_apply_existing_customer_permissions_and_no_delete_action()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.Contains("PermissionCodes.Catalog.Customer.View", controller, StringComparison.Ordinal);
        Assert.Contains("PermissionCodes.Catalog.Customer.Create", controller, StringComparison.Ordinal);
        Assert.Contains("PermissionCodes.Catalog.Customer.Update", controller, StringComparison.Ordinal);
        Assert.Contains("ToggleActive", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("PermissionCodes.Catalog.Customer.Delete", controller, StringComparison.Ordinal);
        Assert.False(Regex.IsMatch(controller, @"IActionResult\s+Delete\s*\(", RegexOptions.IgnoreCase));
    }

    [Fact]
    public void Styles_should_be_scoped_and_mobile_sheets_viewport_safe()
    {
        var style = File.ReadAllText(StylePath);

        Assert.Contains(".customer-index-page.gds-page", style, StringComparison.Ordinal);
        Assert.Contains(".customer-mobile-card", style, StringComparison.Ordinal);
        Assert.Contains(".customer-quick-view", style, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(4", style, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", style, StringComparison.Ordinal);
        Assert.Contains("env(safe-area-inset-bottom)", style, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("GAOAPP_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot) &&
            File.Exists(Path.Combine(configuredRoot, "GaoApp.sln")))
        {
            return configuredRoot;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GaoApp.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate GaoApp repository root.");
    }
}
