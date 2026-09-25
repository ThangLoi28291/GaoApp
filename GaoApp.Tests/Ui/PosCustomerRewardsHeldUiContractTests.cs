using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class PosCustomerRewardsHeldUiContractTests
{
    private static readonly string RepositoryRoot =
        FindRepositoryRoot();

    private static readonly string RenderPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "wwwroot",
            "Admin",
            "js",
            "pos",
            "pos.render.js");

    private static readonly string CustomerScriptPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "wwwroot",
            "Admin",
            "js",
            "pos",
            "pos.customer.js");

    private static readonly string StylePath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "wwwroot",
            "Admin",
            "css",
            "pos",
            "pos-cockpit-v2.css");

    private static readonly string IndexPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "Areas",
            "Admin",
            "Views",
            "POS",
            "Index.cshtml");

    [Fact]
    public void Customer_should_use_compact_benefit_cards_and_existing_action_hooks()
    {
        var render =
            File.ReadAllText(
                RenderPath);

        Assert.Contains(
            "pos-customer-compact",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "pos-customer-benefit-grid",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "pos-customer-benefit-card--points",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "pos-customer-benefit-card--voucher",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-customer-action=\"reward\"",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-customer-action=\"use-voucher\"",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-customer-action=\"clear\"",
            render,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "pos-use-voucher-btn",
            render,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Customer_compact_renderer_should_preserve_all_reward_summary_values()
    {
        var render =
            File.ReadAllText(
                RenderPath);

        Assert.Contains(
            "availablePoints",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "redeemableVoucherCount",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "availableVoucherCount",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "availableVoucherValue",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "KHÁCH SỈ",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "KHÁCH LẺ",
            render,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_customer_reward_and_voucher_write_owners_should_remain_present()
    {
        var customer =
            File.ReadAllText(
                CustomerScriptPath);

        Assert.Contains(
            "/admin/api/customers/redeem-voucher",
            customer,
            StringComparison.Ordinal);

        Assert.Contains(
            "/admin/pos/cart/current/reward-vouchers",
            customer,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-customer-action",
            customer,
            StringComparison.Ordinal);

        Assert.Contains(
            "action === 'reward'",
            customer,
            StringComparison.Ordinal);

        Assert.Contains(
            "action === 'use-voucher'",
            customer,
            StringComparison.Ordinal);

        Assert.Contains(
            "action === 'clear'",
            customer,
            StringComparison.Ordinal);
    }

    [Fact]
    public void P4_styles_should_cover_customer_reward_voucher_and_held_without_new_important_rules()
    {
        var css = LegacyPosCssContractR1.Parse(File.ReadAllText(StylePath));

        // Các surface Customer/Reward/Voucher/Held phải có declaration đúng scope ở ROOT.
        css.Require(".pos-cockpit-v2 .pos-customer-benefit-grid", "display", "grid");
        css.Require(".pos-cockpit-v2 .pos-customer-benefit-grid", "grid-template-columns",
            "repeat(2, minmax(0, 1fr))");
        css.RequireDeclaration("#customerRewardModal .modal-dialog", "max-width");
        css.RequireDeclaration("#useRewardVoucherModal .modal-dialog", "max-width");
        css.RequireDeclaration(".pos-cockpit-v2 .pos-applied-voucher-box", "border");
        css.RequireDeclaration("#heldOrdersModal .modal-dialog", "max-width");
        css.RequireToken("#heldOrdersModal .pos-held-order-card--current", "border-left", "solid");
        css.RequireToken("#heldOrdersModal .pos-held-order-card--other", "border-left", "solid");
    }

    [Fact]
    public void Held_order_modal_and_resume_hooks_should_remain_available()
    {
        var render =
            File.ReadAllText(
                RenderPath);

        var index =
            File.ReadAllText(
                IndexPath);

        Assert.Contains(
            "id=\"heldOrdersModal\"",
            index,
            StringComparison.Ordinal);

        Assert.Contains(
            "id=\"heldOrdersCurrentShiftList\"",
            index,
            StringComparison.Ordinal);

        Assert.Contains(
            "id=\"heldOrdersOtherShiftList\"",
            index,
            StringComparison.Ordinal);

        Assert.Contains(
            "pos-held-order-card--current",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "pos-held-order-card--other",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-resume-btn-id",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-is-other-shift",
            render,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var configured =
            Environment.GetEnvironmentVariable(
                "GAOAPP_REPOSITORY_ROOT");

        if (
            !string.IsNullOrWhiteSpace(
                configured
            ) &&
            File.Exists(
                Path.Combine(
                    configured,
                    "GaoApp.sln"))
        )
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
