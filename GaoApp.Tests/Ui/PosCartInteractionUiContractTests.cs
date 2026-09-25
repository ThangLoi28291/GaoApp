using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class PosCartInteractionUiContractTests
{
    private static readonly string RepositoryRoot =
        FindRepositoryRoot();

    private static readonly string CartPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "Areas",
            "Admin",
            "Views",
            "POS",
            "_CartTable.cshtml");

    private static readonly string RenderPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "wwwroot",
            "Admin",
            "js",
            "pos",
            "pos.render.js");

    private static readonly string OrderPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "wwwroot",
            "Admin",
            "js",
            "pos",
            "pos.order.js");

    private static readonly string StylePath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "wwwroot",
            "Admin",
            "css",
            "pos",
            "pos-cockpit-v2.css");

    [Fact]
    public void Cart_table_should_use_compact_five_column_structure()
    {
        var cart =
            File.ReadAllText(CartPath);

        Assert.Contains(
            "id=\"currentDraftBody\"",
            cart,
            StringComparison.Ordinal);

        Assert.Contains(
            "id=\"paymentListBox\"",
            cart,
            StringComparison.Ordinal);

        Assert.Contains(
            "id=\"linePriceTableModal\"",
            cart,
            StringComparison.Ordinal);

        Assert.Contains(
            "id=\"linePriceTableContent\"",
            cart,
            StringComparison.Ordinal);

        Assert.Contains(
            "pos-col-actions",
            cart,
            StringComparison.Ordinal);

        Assert.Contains(
            "colspan=\"5\"",
            cart,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "pos-col-index",
            cart,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Render_should_use_horizontal_quantity_and_existing_action_hooks()
    {
        var render =
            File.ReadAllText(RenderPath);

        Assert.Contains(
            "parts.join(' · ')",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "class=\"pos-qty-inline\"",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-dec-line-id",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-qty-line-id",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-inc-line-id",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-price-line-id",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-line-discount-id",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-open-qty-line-id",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-remove-line-id",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "pos-line-actions-menu",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "colspan=\"5\"",
            render,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "class=\"pos-line-index\"",
            render,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "class=\"pos-qty-stack\"",
            render,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Order_should_delegate_P2_actions_to_existing_cart_behaviors()
    {
        var order =
            File.ReadAllText(OrderPath);

        Assert.Contains(
            "function closeLineActionMenu",
            order,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-open-qty-line-id",
            order,
            StringComparison.Ordinal);

        Assert.Contains(
            "openQtyEditModalForActiveLine();",
            order,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-inc-line-id",
            order,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-dec-line-id",
            order,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-line-discount-id",
            order,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-remove-line-id",
            order,
            StringComparison.Ordinal);

        Assert.Contains(
            "updateLineQty(lineId, nextQty);",
            order,
            StringComparison.Ordinal);

        Assert.Contains(
            "removeLine(lineId);",
            order,
            StringComparison.Ordinal);
    }

    [Fact]
    public void P2_styles_should_support_compact_1366_rows_without_new_important_rules()
    {
        var css = LegacyPosCssContractR1.Parse(File.ReadAllText(StylePath));

        // Cấu trúc CSS thực, không phụ thuộc comment/heading lịch sử sau consolidate.
        css.Require(".pos-cockpit-v2 .pos-qty-inline", "display", "flex");
        css.Require(".pos-cockpit-v2 .pos-qty-inline", "align-items", "center");
        css.Require(".pos-cockpit-v2 .pos-line-actions-toggle", "display", "inline-flex");
        css.RequireDeclaration(".pos-cockpit-v2 .pos-line-actions-menu", "min-width");
        css.Require(".pos-cockpit-v2 .pos-line-row", "height", "78px", LegacyPosCssContractR1.Compact);
        css.RequireKeyframes("posCockpitLineAdded");
        css.RequireToken(".pos-cockpit-v2 .pos-line-row.pos-line-just-added", "animation", "posCockpitLineAdded");
        css.Require(".pos-cockpit-v2 .pos-line-row.pos-line-just-added", "animation", "none",
            LegacyPosCssContractR1.ReducedMotion);
    }

    [Fact]
    public void Promotion_and_gift_states_should_remain_explicit()
    {
        var render =
            File.ReadAllText(RenderPath);

        Assert.Contains(
            "is-promotion-gift",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "has-line-discount",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "has-promotion",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "pos-line-promo-badge",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "pos-line-combo-badge",
            render,
            StringComparison.Ordinal);

        Assert.Contains(
            "pos-gift-lock-text",
            render,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "fetch(",
            render,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "XMLHttpRequest",
            render,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var configured =
            Environment.GetEnvironmentVariable(
                "GAOAPP_REPOSITORY_ROOT");

        if (
            !string.IsNullOrWhiteSpace(configured) &&
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
