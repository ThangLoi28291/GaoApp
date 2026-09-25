using System.Text.RegularExpressions;
using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class ResponsiveNavigationLayoutContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string ContentLayoutPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Views",
        "Shared",
        "_ContentNavbarLayout.cshtml");

    private static readonly string MasterLayoutPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Views",
        "Shared",
        "_CommonMasterLayout.cshtml");

    private static readonly string ResponsiveNavigationCssPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "wwwroot",
        "Admin",
        "css",
        "gaoapp.responsive-navigation.css");

    private static readonly string MainScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "wwwroot",
        "js",
        "main.js");

    [Fact]
    public void Content_layout_should_render_one_accessible_existing_menu_toggle_when_menu_exists()
    {
        var layout = File.ReadAllText(ContentLayoutPath);

        Assert.Contains("@if (isMenu)", layout, StringComparison.Ordinal);
        var responsiveTriggerCount = Regex.Matches(
            layout,
            @"class=['""][^'""]*gds-responsive-nav-trigger[^'""]*layout-menu-toggle[^'""]*['""]",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant).Count;

        Assert.True(
            responsiveTriggerCount == 1,
            $"Expected one responsive menu trigger, found {responsiveTriggerCount}.");
        Assert.Contains("type=\"button\"", layout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-controls=\"layout-menu\"", layout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-label=\"Mở menu điều hướng\"", layout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gds-responsive-nav-trigger__label", layout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">Menu<", layout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Responsive_navigation_stylesheet_should_load_once_after_design_system_and_before_page_styles()
    {
        var layout = File.ReadAllText(MasterLayoutPath);

        var designSystemIndex = layout.IndexOf(
            "gaoapp.design-system.css",
            StringComparison.OrdinalIgnoreCase);

        var responsiveNavigationIndex = layout.IndexOf(
            "gaoapp.responsive-navigation.css",
            StringComparison.OrdinalIgnoreCase);

        var pageStylesIndex = layout.IndexOf(
            "@RenderSection(\"PageStyles\", required: false)",
            StringComparison.OrdinalIgnoreCase);

        Assert.True(designSystemIndex >= 0, "Design-system stylesheet registration was not found.");
        Assert.True(responsiveNavigationIndex > designSystemIndex, "Responsive navigation CSS must load after the design system.");
        Assert.True(pageStylesIndex > responsiveNavigationIndex, "Page styles must remain the final application style override layer.");
        var responsiveStylesheetCount = Regex.Matches(
            layout,
            @"gaoapp\.responsive-navigation\.css",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant).Count;

        Assert.True(
            responsiveStylesheetCount == 1,
            $"Expected one responsive navigation stylesheet registration, found {responsiveStylesheetCount}.");
    }

    [Fact]
    public void Responsive_navigation_should_use_the_same_lower_left_icon_on_tablet_and_mobile()
    {
        Assert.True(
            File.Exists(ResponsiveNavigationCssPath),
            $"Missing responsive navigation stylesheet: {ResponsiveNavigationCssPath}");

        var css = File.ReadAllText(ResponsiveNavigationCssPath);

        Assert.Contains("@media (min-width: 1200px)", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@media (max-width: 1199.98px)", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@media (max-width: 767.98px)", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inset-inline-start: 1.25rem", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("env(safe-area-inset-left)", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inset-inline-start: 50%", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("translateX(-50%)", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("env(safe-area-inset-bottom)", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("min-block-size: 44px", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("border-radius: 50%", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gds-responsive-nav-trigger__label", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("display: none", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("padding-block-end", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prefers-reduced-motion: reduce", css, StringComparison.OrdinalIgnoreCase);

        var mainScript = File.ReadAllText(MainScriptPath);

        Assert.Contains(
            "querySelectorAll('.layout-menu-toggle')",
            mainScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "window.Helpers.toggleCollapsed()",
            mainScript,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate GaoApp repository root from test output directory.");
    }
}
