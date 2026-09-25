using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class PosPrimeArchitectureUiContractTests
{
    private static readonly string RepositoryRoot =
        FindRepositoryRoot();

    [Fact]
    public void Controller_should_make_prime_canonical_keep_v3_alias_and_expose_legacy_fallback()
    {
        var controller =
            Read(
                "GaoApp.Web",
                "Areas",
                "Admin",
                "Controllers",
                "POSController.cs");

        Assert.Contains(
            "[Route(\"admin/pos\")]",
            controller,
            StringComparison.Ordinal);

        Assert.Equal(
            1,
            CountOccurrences(
                controller,
                "[HttpGet(\"v3\")]"));

        Assert.Equal(
            1,
            CountOccurrences(
                controller,
                "[HttpGet(\"legacy\")]"));

        var indexAction =
            controller.IndexOf(
                "public Task<IActionResult> Index(CancellationToken ct)",
                StringComparison.Ordinal);

        var primeAction =
            controller.IndexOf(
                "public Task<IActionResult> Prime(CancellationToken ct)",
                StringComparison.Ordinal);

        var legacyAction =
            controller.IndexOf(
                "public Task<IActionResult> Legacy(CancellationToken ct)",
                StringComparison.Ordinal);

        var sharedRenderer =
            controller.IndexOf(
                "private async Task<IActionResult> RenderPosPageAsync(",
                StringComparison.Ordinal);

        Assert.True(
            indexAction >= 0 &&
            primeAction > indexAction &&
            legacyAction > primeAction &&
            sharedRenderer > legacyAction,
            "Canonical, v3 alias, legacy fallback, and shared renderer are not in the expected order.");

        var indexBody =
            controller[indexAction..primeAction];

        var primeBody =
            controller[primeAction..legacyAction];

        var legacyBody =
            controller[legacyAction..sharedRenderer];

        Assert.Contains(
            "isPrime: true",
            indexBody,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "isPrime: false",
            indexBody,
            StringComparison.Ordinal);

        Assert.Contains(
            "isPrime: true",
            primeBody,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "isPrime: false",
            primeBody,
            StringComparison.Ordinal);

        Assert.Contains(
            "isPrime: false",
            legacyBody,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "isPrime: true",
            legacyBody,
            StringComparison.Ordinal);

        Assert.Equal(
            3,
            CountOccurrences(
                controller,
                "return RenderPosPageAsync("));

        Assert.Contains(
            "ViewData[\"IsPOSPrime\"] = isPrime;",
            controller,
            StringComparison.Ordinal);

        Assert.Equal(
            2,
            CountOccurrences(
                controller,
                "return View(\"Index\");"));
    }

    [Fact]
    public void Index_should_select_legacy_or_prime_layout_and_keep_one_common_surface_source()
    {
        var index =
            Read(
                "GaoApp.Web",
                "Areas",
                "Admin",
                "Views",
                "POS",
                "Index.cshtml");

        Assert.Contains(
            "? \"_POSPrimeLayout\"",
            index,
            StringComparison.Ordinal);

        Assert.Contains(
            ": \"_ContentNavbarLayout\"",
            index,
            StringComparison.Ordinal);

        Assert.Contains(
            "<partial name=\"_POSPrimeHeader\"",
            index,
            StringComparison.Ordinal);

        Assert.Contains(
            "<partial name=\"_POSPrimeShell\"",
            index,
            StringComparison.Ordinal);

        Assert.Equal(
            1,
            CountOccurrences(
                index,
                "id=\"posShell\""));

        Assert.Equal(
            1,
            CountOccurrences(
                index,
                "id=\"paymentModal\""));

        Assert.Equal(
            1,
            CountOccurrences(
                index,
                "id=\"paymentQrModal\""));

        Assert.Equal(
            1,
            CountOccurrences(
                index,
                "id=\"heldOrdersModal\""));

        Assert.Equal(
            1,
            CountOccurrences(
                index,
                "id=\"posErrorModal\""));
    }

    [Fact]
    public void Prime_layout_should_reuse_vertical_menu_inside_bootstrap_offcanvas()
    {
        var layout =
            Read(
                "GaoApp.Web",
                "Areas",
                "Admin",
                "Views",
                "Shared",
                "_POSPrimeLayout.cshtml");

        Assert.Contains(
            "Layout = \"_CommonMasterLayout\";",
            layout,
            StringComparison.Ordinal);

        Assert.Contains(
            "id=\"posPrimeAdminDrawer\"",
            layout,
            StringComparison.Ordinal);

        Assert.Contains(
            "offcanvas offcanvas-start",
            layout,
            StringComparison.Ordinal);

        Assert.Contains(
            "Sections/Menu/_VerticalMenu",
            layout,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "layout-menu-toggle\"",
            layout,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "_ContentNavbarLayout",
            layout,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Prime_shell_should_reuse_existing_pos_partials_without_copying_business_forms()
    {
        var shell =
            Read(
                "GaoApp.Web",
                "Areas",
                "Admin",
                "Views",
                "POS",
                "_POSPrimeShell.cshtml");

        foreach (var partial in new[]
        {
            "_Toolbar",
            "_CartTable",
            "_CustomerBox",
            "_DraftMetaPanel",
            "_SummaryPanel",
            "_SummaryActionsPanel"
        })
        {
            Assert.Equal(
                1,
                CountOccurrences(
                    shell,
                    $"name=\"{partial}\""));
        }

        Assert.Contains(
            "pos-prime-workspace",
            shell,
            StringComparison.Ordinal);

        Assert.Contains(
            "pos-prime-context",
            shell,
            StringComparison.Ordinal);

        Assert.Contains(
            "pos-prime-sale",
            shell,
            StringComparison.Ordinal);

        Assert.Contains(
            "pos-prime-checkout",
            shell,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "id=\"paymentModal\"",
            shell,
            StringComparison.Ordinal);
    }

    private static string Read(
        params string[] parts)
    {
        return File.ReadAllText(
            Path.Combine(
                new[] { RepositoryRoot }
                    .Concat(parts)
                    .ToArray()));
    }

    private static int CountOccurrences(
        string source,
        string value)
    {
        var count = 0;
        var index = 0;

        while ((index = source.IndexOf(
            value,
            index,
            StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var configured =
            Environment.GetEnvironmentVariable(
                "GAOAPP_REPOSITORY_ROOT");

        if (
            !string.IsNullOrWhiteSpace(configured)
            && File.Exists(
                Path.Combine(
                    configured,
                    "GaoApp.sln")))
        {
            return configured;
        }

        foreach (var start in new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        })
        {
            var directory =
                new DirectoryInfo(start);

            while (directory is not null)
            {
                if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "GaoApp.sln")))
                {
                    return directory.FullName;
                }

                directory =
                    directory.Parent;
            }
        }

        throw new InvalidOperationException(
            "Could not locate GaoApp repository root.");
    }
}
