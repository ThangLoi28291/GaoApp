using System.Text.RegularExpressions;
using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class GaoAppDesignSystemSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string LayoutPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Views",
        "Shared",
        "_CommonMasterLayout.cshtml");

    private static readonly string DesignSystemCssPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "wwwroot",
        "Admin",
        "css",
        "gaoapp.design-system.css");

    [Fact]
    public void DesignSystem_ShouldBeRegisteredExactlyOnce()
    {
        Assert.True(
            File.Exists(DesignSystemCssPath),
            $"Missing Wave 0 design-system stylesheet: {DesignSystemCssPath}");

        var layout = File.ReadAllText(LayoutPath);

        var count = Regex.Matches(
            layout,
            @"gaoapp\.design-system\.css",
            RegexOptions.IgnoreCase).Count;

        Assert.Equal(1, count);
    }

    [Fact]
    public void DesignSystem_ShouldLoadAfterActiveAdminStylesAndBeforePageStyles()
    {
        var layout = File.ReadAllText(LayoutPath);

        var sharedStylesIndex = layout.IndexOf(
            @"@await Html.PartialAsync(""Sections/_Styles"")",
            StringComparison.OrdinalIgnoreCase);

        var vendorStylesIndex = layout.IndexOf(
            @"@RenderSection(""VendorStyles"", required: false)",
            StringComparison.OrdinalIgnoreCase);

        var toastrIndex = layout.IndexOf(
            "toastr.min.css",
            StringComparison.OrdinalIgnoreCase);

        var designSystemIndex = layout.IndexOf(
            "gaoapp.design-system.css",
            StringComparison.OrdinalIgnoreCase);

        var pageStylesIndex = layout.IndexOf(
            @"@RenderSection(""PageStyles"", required: false)",
            StringComparison.OrdinalIgnoreCase);

        Assert.True(
            sharedStylesIndex >= 0,
            "Active shared _Styles registration was not found.");

        Assert.True(
            vendorStylesIndex > sharedStylesIndex,
            "VendorStyles must load after the shared _Styles partial.");

        Assert.True(
            toastrIndex > vendorStylesIndex,
            "Toastr CSS must load after VendorStyles.");

        Assert.True(
            designSystemIndex > toastrIndex,
            "gaoapp.design-system.css must load after the currently active shared/Vendor/Toastr styles.");

        Assert.True(
            pageStylesIndex > designSystemIndex,
            "PageStyles must load after gaoapp.design-system.css.");

        Assert.DoesNotContain(
            "gaoapp.ui.css",
            layout,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesignSystem_ShouldRemainScopedAndSelfContained()
    {
        Assert.True(
            File.Exists(DesignSystemCssPath),
            $"Missing Wave 0 design-system stylesheet: {DesignSystemCssPath}");

        var css = File.ReadAllText(DesignSystemCssPath);

        Assert.Contains("[data-gao-ui=\"modern\"]", css);
        Assert.Contains(".gds-", css);

        Assert.DoesNotContain(
            "@import",
            css,
            StringComparison.OrdinalIgnoreCase);

        Assert.False(
            Regex.IsMatch(
                css,
                @"https?://",
                RegexOptions.IgnoreCase),
            "Design-system CSS must not introduce an external dependency.");

        Assert.False(
            Regex.IsMatch(
                css,
                @"--bs-[A-Za-z0-9_-]+\s*:",
                RegexOptions.IgnoreCase),
            "Design-system CSS must not redefine Bootstrap variables.");

        Assert.False(
            Regex.IsMatch(
                css,
                @"(?m)^\s*\.(?:btn|card|table|modal|form-control|badge)(?=[\s:{.#\[])"),
            "Design system must not globally restyle Bootstrap components.");

        Assert.False(
            Regex.IsMatch(
                css,
                @"(?m)^\s*(?:body|h[1-6])(?=[\s,{.#:\[])",
                RegexOptions.IgnoreCase),
            "Design system must not globally restyle document elements.");
    }

    [Fact]
    public void BusinessViewOptIn_ShouldMatchApprovedProgressiveRollout()
    {
        var viewsRoot = Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "Areas",
            "Admin",
            "Views");

        var approvedOptIns = new[]
        {
            "AdminMenus/Index.cshtml",
            "AttributeValue/Index.cshtml",
            "Brand/Index.cshtml",
           "Category/Index.cshtml",
"Customer/Edit.cshtml",
"Customer/Index.cshtml",
"CustomerDebt/Collections.cshtml",
"CustomerDebt/Receipt.cshtml",
"CustomerDebt/Index.cshtml",
"CustomerDeposit/Index.cshtml",
"InventoryAdjustmentDocuments/Index.cshtml",
            "InventoryInquiry/Index.cshtml",
            "InventoryLedger/Index.cshtml",
            "Invoice/Index.cshtml",
            "InvoiceInputStock/Index.cshtml",
            "MediaLibrary/Index.cshtml",
            "Product/Index.cshtml",
            "ProductAttribute/Index.cshtml",
 "PurchaseOrders/Index.cshtml",
"PurchaseRequests/Index.cshtml",
"PurchaseRequests/Details.cshtml",
"PurchaseRequests/Edit.cshtml",
"PurchaseRequests/Prepare.cshtml",
"RewardVouchers/Index.cshtml",
            "Roles/Index.cshtml",
            "SalesExecutiveReport/Index.cshtml",
            "SalesReport/Index.cshtml",
            "StockCountPages/Index.cshtml",
            "StockDocumentManagement/Index.cshtml",
            "StockTransfer/Index.cshtml",
            "StoreBankAccounts/Index.cshtml",
            "Supplier/Index.cshtml",
            "Tax/Index.cshtml",
            "Unit/Index.cshtml",
            "UserInStores/Index.cshtml",
            "WarehouseManagement/Index.cshtml",
            "WarehouseReceiving/Index.cshtml"
        }
        .OrderBy(
            static path => path,
            StringComparer.OrdinalIgnoreCase)
        .ToArray();

        var actualOptIns = Directory
            .EnumerateFiles(
                viewsRoot,
                "*.cshtml",
                SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}Shared{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            .Where(path =>
                File.ReadAllText(path).Contains(
                    "data-gao-ui=\"modern\"",
                    StringComparison.OrdinalIgnoreCase))
            .Select(path => Path
                .GetRelativePath(viewsRoot, path)
                .Replace('\\', '/'))
            .OrderBy(
                static path => path,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(
            approvedOptIns,
            actualOptIns);
    }

    [Fact]
    public void ClosedReferenceIndexes_ShouldUseFluidMenuLayoutAndInheritedPageSpacing()
    {
        var viewsRoot = Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "Areas",
            "Admin",
            "Views");

        var referenceIndexes = new[]
        {
            "AttributeValue/Index.cshtml",
            "Brand/Index.cshtml",
            "Category/Index.cshtml",
            "ProductAttribute/Index.cshtml",
            "Supplier/Index.cshtml",
            "Tax/Index.cshtml",
            "Unit/Index.cshtml"
        };

        foreach (var relativePath in referenceIndexes)
        {
            var view = File.ReadAllText(Path.Combine(
                viewsRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

            Assert.Contains(
                "ViewData[\"container\"] = \"container-fluid\"",
                view,
                StringComparison.Ordinal);
            Assert.Contains(
                "ViewData[\"menuFixed\"] = \"layout-menu-fixed\"",
                view,
                StringComparison.Ordinal);
            Assert.Matches(
                new Regex(
                    "data-layout-consistency[^>]*>\\s*<div[\\s\\S]*?class=\"gds-page\"",
                    RegexOptions.IgnoreCase),
                view);
        }
    }

    [Fact]
    public void MotionRules_ShouldRespectReducedMotion()
    {
        Assert.True(
            File.Exists(DesignSystemCssPath),
            $"Missing Wave 0 design-system stylesheet: {DesignSystemCssPath}");

        var css = File.ReadAllText(DesignSystemCssPath);

        var containsMotion =
            css.Contains(
                "transition",
                StringComparison.OrdinalIgnoreCase) ||
            css.Contains(
                "@keyframes",
                StringComparison.OrdinalIgnoreCase);

        if (containsMotion)
        {
            Assert.Contains(
                "prefers-reduced-motion",
                css,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (
                File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "GaoApp.sln"))
            )
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate GaoApp repository root from test output directory.");
    }
}
