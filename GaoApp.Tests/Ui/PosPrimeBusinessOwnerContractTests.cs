using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class PosPrimeBusinessOwnerContractTests
{
    private static readonly string RepositoryRoot =
        FindRepositoryRoot();

    [Fact]
    public void Index_should_keep_existing_business_modules_single_and_in_order()
    {
        var index =
            Read(
                "GaoApp.Web",
                "Areas",
                "Admin",
                "Views",
                "POS",
                "Index.cshtml");

        var scripts = new[]
        {
            "pos.state.js",
            "pos.dom.js",
            "pos.common.js",
            "pos.error.js",
            "pos.render.js",
            "pos.customer.js",
            "pos.payment.js",
            "pos.barcode.js",
            "pos.order.js",
            "pos.keyboard.js",
            "pos.cockpit.js",
            "pos.app.js"
        };

        var previousIndex = -1;

        foreach (var script in scripts)
        {
            Assert.Equal(
                1,
                CountOccurrences(
                    index,
                    $"src=\"~/Admin/js/pos/{script}\""));

            var currentIndex =
                index.IndexOf(
                    $"src=\"~/Admin/js/pos/{script}\"",
                    StringComparison.Ordinal);

            Assert.True(
                currentIndex > previousIndex,
                $"{script} is not in the expected load order.");

            previousIndex =
                currentIndex;
        }

        Assert.Equal(
            1,
            CountOccurrences(
                index,
                "window.PosApp.create()"));

        // P3 adds one presentation adapter after all unchanged business modules.
        Assert.Equal(1, CountOccurrences(index, "src=\"~/Admin/js/pos/pos.prime.js\""));
        Assert.True(index.IndexOf("src=\"~/Admin/js/pos/pos.prime.js\"", StringComparison.Ordinal) > previousIndex);
        Assert.Equal(1, CountOccurrences(index, "window.PosPrime.mount()"));
        Assert.True(index.IndexOf("window.PosPrime.mount()", StringComparison.Ordinal)
            < index.IndexOf("window.PosApp.create()", StringComparison.Ordinal));

        foreach (var seam in new[] { "PRIME_ADAPTER_SCRIPT", "PRIME_ADAPTER_BOOTSTRAP" })
        {
            var begin = index.IndexOf(seam + "_BEGIN", StringComparison.Ordinal);
            var end = index.IndexOf(seam + "_END", StringComparison.Ordinal);
            Assert.True(begin >= 0 && end > begin);
            Assert.Contains("@if (isPrime)", index[begin..end], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Prime_presentation_files_should_not_own_http_storage_or_recent_products()
    {
        var source =
            string.Join(
                "\n",
                Read(
                    "GaoApp.Web",
                    "Areas",
                    "Admin",
                    "Views",
                    "Shared",
                    "_POSPrimeLayout.cshtml"),
                Read(
                    "GaoApp.Web",
                    "Areas",
                    "Admin",
                    "Views",
                    "POS",
                    "_POSPrimeHeader.cshtml"),
                Read(
                    "GaoApp.Web",
                    "Areas",
                    "Admin",
                    "Views",
                    "POS",
                    "_POSPrimeShell.cshtml"),
                Read(
                    "GaoApp.Web",
                    "wwwroot",
                    "Admin",
                    "css",
                    "pos",
                    "pos-prime.css"),
                Read(
                    "GaoApp.Web", "wwwroot", "Admin", "js", "pos", "pos.prime.js"));

        Assert.DoesNotContain(
            "fetch(",
            source,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "XMLHttpRequest",
            source,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "localStorage",
            source,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "sessionStorage",
            source,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "/admin/pos/cart",
            source,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "Recent Products",
            source,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "Sản phẩm gần đây",
            source,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Controller_should_preserve_existing_pos_business_endpoint_owners()
    {
        var controller =
            Read(
                "GaoApp.Web",
                "Areas",
                "Admin",
                "Controllers",
                "POSController.cs");

        var expectedRouteCounts =
            new Dictionary<string, int>
            {
                ["cart/current/payments"] = 1,
                ["cart/current/payment-and-finalize"] = 1,
                ["cart/current/finalize"] = 1,
                ["cart/current/hold"] = 1,
                ["cart/current/cancel"] = 1,
                ["cart/current/reward-vouchers"] = 2,
                ["screen"] = 1
            };

        foreach (var item in expectedRouteCounts)
        {
            var actual =
                CountOccurrences(
                    controller,
                    $"[HttpGet(\"{item.Key}\")]")
                + CountOccurrences(
                    controller,
                    $"[HttpPost(\"{item.Key}\")]")
                + CountOccurrences(
                    controller,
                    $"[HttpDelete(\"{item.Key}\")]");

            Assert.Equal(
                item.Value,
                actual);
        }

        Assert.Equal(
            1,
            CountOccurrences(
                controller,
                "[HttpGet(\"v3\")]"));
    }

    [Fact]
    public void Prime_css_should_be_scoped_and_should_not_use_brute_force_rules()
    {
        var style =
            Read(
                "GaoApp.Web",
                "wwwroot",
                "Admin",
                "css",
                "pos",
                "pos-prime.css");

        Assert.Contains(
            ".pos-prime-layout",
            style,
            StringComparison.Ordinal);

        Assert.Contains(
            ".pos-cockpit-v2.pos-prime",
            style,
            StringComparison.Ordinal);

        Assert.Contains(
            ".pos-prime-admin-drawer",
            style,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "!important",
            style,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "@keyframes",
            style,
            StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void Prime_adapter_should_move_original_controls_and_not_replace_business_state()
    {
        var script = Read("GaoApp.Web", "wwwroot", "Admin", "js", "pos", "pos.prime.js");
        Assert.Contains("host.appendChild(node)", script, StringComparison.Ordinal);
        Assert.Contains("marker.parentNode.insertBefore(node, marker.nextSibling)", script, StringComparison.Ordinal);
        Assert.Contains("byId('btnAddPayment')", script, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "cloneNode(", ".innerHTML =", ".outerHTML =", "PosApp.create(", ".init()",
            "postJson", "patchJson", "deleteJson", "XMLHttpRequest", "fetch(", "setInterval(",
            "localStorage", "sessionStorage", "currentDraft =", "busyScopes =",
            ".value =", ".disabled ="
        })
            Assert.DoesNotContain(forbidden, script, StringComparison.Ordinal);
    }

    [Fact]
    public void Prime_dock_should_mirror_canonical_balance_without_a_mobile_money_engine()
    {
        var script = Read("GaoApp.Web", "wwwroot", "Admin", "js", "pos", "pos.prime.js");
        Assert.Contains("balance: byId('sumBalance')", script, StringComparison.Ordinal);
        Assert.Contains("setText(elements.dockBalance, text(elements.balance) || '—')", script, StringComparison.Ordinal);
        Assert.DoesNotContain("parseFloat(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("grandTotal -", script, StringComparison.Ordinal);
        Assert.DoesNotContain("paidTotal +", script, StringComparison.Ordinal);
        Assert.DoesNotContain("referenceCode:", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Prime_overlays_should_wait_for_hidden_and_guard_background_shortcuts()
    {
        var script = Read("GaoApp.Web", "wwwroot", "Admin", "js", "pos", "pos.prime.js");
        Assert.Contains("'hidden.bs.offcanvas'", script, StringComparison.Ordinal);
        Assert.Contains("'shown.bs.offcanvas'", script, StringComparison.Ordinal);
        Assert.Contains("listen(window, 'keydown', onKeydown, true)", script, StringComparison.Ordinal);
        Assert.Contains("event.stopImmediatePropagation()", script, StringComparison.Ordinal);
        Assert.Contains("const current = resolve()", script, StringComparison.Ordinal);
        Assert.Contains("if (!enabled(current)) return", script, StringComparison.Ordinal);
        Assert.DoesNotContain("setTimeout(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("setInterval(", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Prime_adapter_should_be_idempotent_and_release_owned_resources()
    {
        var script = Read("GaoApp.Web", "wwwroot", "Admin", "js", "pos", "pos.prime.js");
        Assert.Contains("if (instance) return instance", script, StringComparison.Ordinal);
        Assert.Contains("if (event.persisted) suspend()", script, StringComparison.Ordinal);
        Assert.Contains("if (event.persisted) resume()", script, StringComparison.Ordinal);
        Assert.Contains("observers.pop().disconnect()", script, StringComparison.Ordinal);
        Assert.Contains("removers.pop()()", script, StringComparison.Ordinal);
        Assert.Contains("window.cancelAnimationFrame", script, StringComparison.Ordinal);
        Assert.Contains("restore(elements.addPayment)", script, StringComparison.Ordinal);
        Assert.Contains("submitHost.remove()", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Mobile_table_class_should_be_visual_only_and_leave_active_row_owner_unchanged()
    {
        // The legacy visual class has forced table display rules. A same-node
        // presentation alias is safe only while business owners do not query it.
        foreach (var owner in new[] { "state", "dom", "common", "error", "render", "customer", "payment", "barcode", "order", "keyboard", "cockpit", "app" })
        {
            var source = Read("GaoApp.Web", "wwwroot", "Admin", "js", "pos", $"pos.{owner}.js");
            Assert.DoesNotContain("pos-line-table", source, StringComparison.Ordinal);
        }
        var script = Read("GaoApp.Web", "wwwroot", "Admin", "js", "pos", "pos.prime.js");
        Assert.Contains("classList.toggle('pos-prime-mobile-table', cardQuery.matches)", script, StringComparison.Ordinal);
        Assert.Contains("classList.toggle('pos-line-table', originalTableClass)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("classList.remove('is-active-line')", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_feedback_should_use_acknowledged_drafts_without_mutating_sale_values()
    {
        var adapter = Read("GaoApp.Web", "wwwroot", "Admin", "js", "pos", "pos.prime.js");
        var feedback = Read("GaoApp.Web", "wwwroot", "Admin", "js", "pos", "pos.scan-feedback.js");
        var barcode = Read("GaoApp.Web", "wwwroot", "Admin", "js", "pos", "pos.barcode.js");
        Assert.DoesNotContain("barcodeReceipt()", adapter, StringComparison.Ordinal);
        Assert.Contains("feedback?.confirmed(attempt, draft)", barcode, StringComparison.Ordinal);
        Assert.Contains("feedback?.failed(attempt, result.error", barcode, StringComparison.Ordinal);
        Assert.Contains("!line.isPromotionGift", feedback, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(", feedback, StringComparison.Ordinal);
        Assert.DoesNotContain("postJson(", feedback, StringComparison.Ordinal);
        Assert.DoesNotContain("line.quantity =", feedback, StringComparison.Ordinal);
        Assert.DoesNotContain("line.unitPrice =", feedback, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_history_should_be_scoped_to_operator_terminal_and_cart_and_not_expire_with_highlight()
    {
        var feedback = Read("GaoApp.Web", "wwwroot", "Admin", "js", "pos", "pos.scan-feedback.js");
        Assert.Contains("shell.dataset.storeId, shell.dataset.terminalId, shell.dataset.userId", feedback, StringComparison.Ordinal);
        Assert.Contains("carts[orderId(draft)]", feedback, StringComparison.Ordinal);
        Assert.Contains("slice(0,10)", feedback, StringComparison.Ordinal);
        Assert.DoesNotContain("new Audio", feedback, StringComparison.Ordinal);
        Assert.DoesNotContain("scrollIntoView", feedback, StringComparison.Ordinal);
    }

    [Fact]
    public void P4_held_utility_should_decorate_current_canonical_launcher_without_new_click_owner()
    {
        var script = Read("GaoApp.Web", "wwwroot", "Admin", "js", "pos", "pos.prime.js");
        foreach (var token in new[]
        {
            "heldList.querySelector('#btnOpenHeldOrders')", ".pos-held-summary-launcher__total",
            "heldDecoration?.button !== button", "restoreHeldDecoration()",
            "button.setAttribute('aria-keyshortcuts', 'F6')", "syncHeldPresentation()"
        }) Assert.Contains(token, script, StringComparison.Ordinal);
        Assert.DoesNotContain("renderHeldList(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("getHeldOrders(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("heldList.innerHTML", script, StringComparison.Ordinal);
    }

    [Fact]
    public void P4_recovery_should_yield_to_user_input_overlays_phone_and_background_tab()
    {
        var script = Read("GaoApp.Web", "wwwroot", "Admin", "js", "pos", "pos.prime.js");
        foreach (var token in new[]
        {
            "version !== interactionVersion", "!document.hasFocus()",
            "document.visibilityState === 'hidden'", "activeSheet || businessModalOpen()",
            "inputLike(current)", "if (phoneQuery.matches)", "!inputLike(opener)",
            "queueFocusRecovery(event.target, opener)", "closingModals.delete(event.target)",
            "cancelFocusRecovery()", "window.cancelAnimationFrame(recoveryFrame)"
        }) Assert.Contains(token, script, StringComparison.Ordinal);
        Assert.DoesNotContain(".blur()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("document.body.focus(", script, StringComparison.Ordinal);
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
