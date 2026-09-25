using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class RewardSettingsAdminUiContractTests
{
    private static readonly string RepositoryRoot =
        FindRepositoryRoot();

    [Fact]
    public void Settings_controller_should_require_system_setting_update_permission()
    {
        var source =
            Read(
                "GaoApp.Web",
                "Areas",
                "Admin",
                "Controllers",
                "RewardSettingsController.cs");

        Assert.Contains(
            "admin/reward-vouchers/settings",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "PermissionCodes.System.Setting.Update",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "[HttpGet(\"\")]",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "[HttpPost(\"\")]",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "[ValidateAntiForgeryToken]",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Delete",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_form_should_not_accept_editable_store_id()
    {
        var view =
            Read(
                "GaoApp.Web",
                "Areas",
                "Admin",
                "Views",
                "RewardSettings",
                "Index.cshtml");

        Assert.Contains(
            "MoneyPerPoint",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "PointsPerVoucher",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "VoucherValue",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "ConfirmRateChange",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "RowVersion",
            view,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "name=\"StoreId\"",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "Delete",
            view,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_page_should_preview_policy_and_confirm_rate_changes()
    {
        var script =
            Read(
                "GaoApp.Web",
                "wwwroot",
                "Admin",
                "js",
                "reward-settings.js");

        Assert.Contains(
            "hasRateChanged",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "previewRequiredAmount",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "previewRewardRate",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "rewardSettingsConfirmModal",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "ConfirmRateChange",
            Read(
                "GaoApp.Web",
                "Areas",
                "Admin",
                "Views",
                "RewardSettings",
                "Index.cshtml"),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "fetch(",
            script,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reward_voucher_index_should_expose_settings_only_by_permission()
    {
        var view =
            Read(
                "GaoApp.Web",
                "Areas",
                "Admin",
                "Views",
                "RewardVouchers",
                "Index.cshtml");

        Assert.Contains(
            "PermissionCodes.System.Setting.Update",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "RewardSettings",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "Cấu hình tích điểm",
            view,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_styles_should_be_responsive_without_important_rules()
    {
        var style =
            Read(
                "GaoApp.Web",
                "wwwroot",
                "Admin",
                "css",
                "reward-settings.css");

        Assert.Contains(
            ".reward-settings-layout",
            style,
            StringComparison.Ordinal);

        Assert.Contains(
            ".reward-settings-preview-card",
            style,
            StringComparison.Ordinal);

        Assert.Contains(
            ".reward-settings-confirm",
            style,
            StringComparison.Ordinal);

        Assert.Contains(
            "@media (max-width: 991.98px)",
            style,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "!important",
            style,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(
        params string[] parts)
        => File.ReadAllText(
            Path.Combine(
                new[] { RepositoryRoot }
                    .Concat(parts)
                    .ToArray()));

    private static string FindRepositoryRoot()
    {
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
        }

        throw new InvalidOperationException(
            "Could not locate GaoApp repository root.");
    }
}