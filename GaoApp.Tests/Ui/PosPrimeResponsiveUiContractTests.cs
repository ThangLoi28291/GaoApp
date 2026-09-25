using System.Text.RegularExpressions;
using Xunit;

namespace GaoApp.Tests.Ui;

/// <summary>
/// V3-P3 source contracts; retain P2 desktop/F01 protections. These do not replace browser layout, clipping,
/// Bootstrap interaction or Human visual verification.
/// </summary>
public sealed class PosPrimeResponsiveUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private const string IndexPath = "GaoApp.Web/Areas/Admin/Views/POS/Index.cshtml";
    private const string StylePath = "GaoApp.Web/wwwroot/Admin/css/pos/pos-prime.css";
    private const string LegacyStyle = "~/Admin/css/pos/pos-cockpit-v2.css";
    private const string PrimeStyle = "~/Admin/css/pos/pos-prime.css";
    private const string SharedStyle = "~/Admin/css/pos/pos.css";

    [Fact]
    public void Styles_should_select_prime_or_legacy_exclusively_with_shared_pos_foundation()
    {
        var index = Read(IndexPath);
        var section = BracedBlock(index, "@section VendorStyles", out _);
        var conditionalStart = section.IndexOf("@if (isPrime)", StringComparison.Ordinal);
        Assert.True(conditionalStart >= 0, "VendorStyles must branch on the existing isPrime mode.");

        var common = section[..conditionalStart];
        var prime = BracedBlock(section, "@if (isPrime)", out var endOfPrime);
        var legacy = BracedBlock(section[endOfPrime..], "else", out _);

        Assert.Equal(new[] { "~/Admin/css/pos/pos.scan-feedback.css", "~/Admin/css/pos/pos.qr-history.css", SharedStyle }, StyleLinks(common));
        Assert.Equal(new[] { PrimeStyle }, StyleLinks(prime));
        Assert.Equal(new[] { LegacyStyle }, StyleLinks(legacy));
        Assert.Equal(1, StyleLinks(section).Count(path => path == PrimeStyle));
        Assert.Equal(1, StyleLinks(section).Count(path => path == LegacyStyle));
        Assert.DoesNotContain(LegacyStyle, prime, StringComparison.Ordinal);
        Assert.DoesNotContain(PrimeStyle, legacy, StringComparison.Ordinal);
    }

    [Fact]
    public void Index_should_preserve_core_bootstrap_script_order_and_payment_feature_contracts()
    {
        // The retired whole-page hash described POS before the QR/ACB features.
        // Keep explicit script/initialization boundaries and pair with the existing
        // canonical modal-ID and responsive/Prime ownership tests in this suite.
        var index = Read(IndexPath);
        foreach (var seam in new[] { "PRIME_ADAPTER_SCRIPT", "PRIME_ADAPTER_BOOTSTRAP" })
        {
            var pattern = @"(?m)^[ \t]*@\* " + seam + @"_BEGIN \*@\n[\s\S]*?^[ \t]*@\* " + seam + @"_END \*@\n";
            var matches = Regex.Matches(index, pattern, RegexOptions.CultureInvariant);
            Assert.Single(matches.Cast<Match>());
            Assert.Contains("@if (isPrime)", matches[0].Value, StringComparison.Ordinal);
        }
        Assert.Single(Regex.Matches(index, @"@Html\.AntiForgeryToken\(\)").Cast<Match>());
        Assert.Single(Regex.Matches(index, @"const app = window\.PosApp\.create\(\);").Cast<Match>());
        Assert.Single(Regex.Matches(index, @"await app\.init\(\);").Cast<Match>());
        var scripts = Regex.Matches(index, "<script\\s+src=\"~/Admin/js/pos/([^\"]+)\"")
            .Cast<Match>().Select(x => x.Groups[1].Value).ToArray();
        Assert.Equal(new[] {
            "qrcodegen.js", "pos.offline.core.js", "pos.offline.js",
            "pos.state.js", "pos.dom.js", "pos.common.js", "pos.error.js", "pos.render.js", "pos.scan-feedback.js", "pos.customer.js",
            "pos.acb.js", "pos.qr-history.js", "pos.payment.js", "pos.barcode.js", "pos.order.js",
            "pos.keyboard.js", "pos.cockpit.js", "pos.app.js", "pos.prime.js"
        }, scripts);
        foreach (var id in new[] { "paymentQrHistory", "btnRefreshPaymentQrHistory", "btnReopenLatestPaymentQr",
            "paymentQrHistoryStatus", "paymentQrHistorySummary", "paymentQrHistoryList", "btnCreatePaymentQr",
            "paymentQrRequestCode", "acbPaymentCountdown", "acbPaymentCountdownLabel", "acbPaymentCountdownValue",
            "acbPaymentCheckStatus", "paymentQrPopupTitle", "paymentQrShortcuts" })
            Assert.Single(Regex.Matches(index, "id=\"" + id + "\"").Cast<Match>());
        Assert.Contains("id=\"paymentQrHistoryStatus\" class=\"pos-qr-history__status\" role=\"status\" aria-live=\"polite\"", index);
        Assert.Contains("id=\"acbPaymentCountdownValue\" role=\"timer\" aria-live=\"off\"", index);
        Assert.True(Array.IndexOf(scripts, "pos.acb.js") < Array.IndexOf(scripts, "pos.payment.js"));
    }
    [Fact]
    public void Every_prime_css_selector_should_have_an_explicit_prime_boundary()
    {
        var style = Read(StylePath);
        var rules = CssRules(style).ToArray();
        Assert.True(rules.Length > 50, "Do not allow an empty stylesheet to satisfy scoping checks.");
        foreach (var rule in rules)
        {
            foreach (var selector in rule.Selector.Split(','))
            {
                var value = selector.Trim();
                var scoped = value.StartsWith(".pos-prime ", StringComparison.Ordinal)
                    || value.StartsWith(".pos-cockpit-v2.pos-prime", StringComparison.Ordinal)
                    || value.StartsWith(".pos-prime-layout", StringComparison.Ordinal)
                    || value.StartsWith(".pos-prime-admin-drawer", StringComparison.Ordinal)
                    || value.StartsWith(".pos-prime-menu-host", StringComparison.Ordinal);
                Assert.True(scoped, $"Unscoped Prime selector: {value}");
            }
        }
        Assert.DoesNotContain("!important", style, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@keyframes", style, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", style, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("backdrop-filter", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Desktop_workspace_should_keep_three_zones_and_one_primary_cart_scroll()
    {
        var style = Read(StylePath);
        var grid = BaseRule(style, ".pos-prime .pos-prime-workspace");
        Assert.Equal("\"context sale checkout\"", Declaration(grid, "grid-template-areas"));
        Assert.Equal("232px minmax(0,1fr) 350px", Declaration(grid, "grid-template-columns"));

        var scroll = BaseRule(style, ".pos-prime .pos-prime-cart__body .pos-cart-scroll");
        Assert.Equal("auto", Declaration(scroll, "overflow"));
        Assert.Equal("none", Declaration(scroll, "max-height"));
        Assert.Equal("0", Declaration(scroll, "min-height"));

        var summary = BaseRule(style, ".pos-prime .pos-prime-checkout__summary");
        Assert.Equal("0 0 auto", Declaration(summary, "flex"));
        Assert.Equal("visible", Declaration(summary, "overflow"));
        Assert.DoesNotContain("pos-cockpit-checkout__customer", style, StringComparison.Ordinal);
        Assert.DoesNotContain("pos-cockpit-checkout__details", style, StringComparison.Ordinal);
    }

    [Fact]
    public void Critical_cart_text_and_photos_should_not_use_micro_sized_presentation()
    {
        var style = Read(StylePath);
        Assert.Equal("16px", Declaration(BaseRule(style, ".pos-prime .pos-line-name"), "font-size"));
        Assert.Equal("13px", Declaration(BaseRule(style, ".pos-prime .pos-line-submeta"), "font-size"));
        Assert.Equal("15px", Declaration(BaseRule(style, ".pos-prime .pos-line-money"), "font-size"));
        Assert.Equal("17px", Declaration(BaseRule(style, ".pos-prime .pos-line-total"), "font-size"));
        var image = BaseRule(style, ".pos-prime .pos-line-thumb-wrap");
        Assert.Equal("64px", Declaration(image, "width"));
        Assert.Equal("64px", Declaration(image, "height"));
        Assert.Equal("contain", Declaration(BaseRule(style, ".pos-prime .pos-line-thumb"), "object-fit"));
        Assert.Equal("normal", Declaration(BaseRule(style, ".pos-prime .pos-line-name"), "white-space"));
    }

    [Fact]
    public void Compact_desktop_should_reduce_spacing_not_critical_font_sizes()
    {
        var style = Read(StylePath);
        var compact = BracedBlock(style,
            "@media (min-width: 1200px) and (max-height: 820px)", out _);
        Assert.NotEmpty(compact);
        Assert.DoesNotContain("font-size:", compact, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("display: none", compact, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("overflow: hidden", compact, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Common_modal_surface_styles_should_survive_removal_of_cockpit_stylesheet()
    {
        var style = Read(StylePath);
        foreach (var selector in new[]
        {
            ".pos-prime-layout .pos-payment-workspace-modal .pos-payment-workspace-dialog",
            ".pos-prime-layout .pos-payment-workspace__body",
            ".pos-prime-layout .pos-payment-workspace__footer",
            ".pos-prime-layout .pos-payment-method-button",
            ".pos-prime-layout .pos-payment-qr-dialog",
            ".pos-prime-layout #customerRewardModal .modal-dialog",
            ".pos-prime-layout #useRewardVoucherModal .modal-dialog",
            ".pos-prime-layout #heldOrdersModal .modal-dialog"
        })
        {
            Assert.NotEmpty(BaseRule(style, selector));
        }
        Assert.Contains("[data-payment-method=\"0\"]", style, StringComparison.Ordinal);
        Assert.Contains("#btnFinalizeFromPaymentModal:disabled", style, StringComparison.Ordinal);
    }

    [Fact]
    public void Receipt_summary_should_keep_single_column_math_and_source_driven_action_states()
    {
        var style = Read(StylePath);
        var breakdown = BaseRule(style, ".pos-prime .pos-cockpit-summary-breakdown");
        Assert.Equal("minmax(0,1fr)", Declaration(breakdown, "grid-template-columns"));
        var money = BaseRule(style, ".pos-prime .pos-summary-hero__value");
        Assert.Equal("40px", Declaration(money, "font-size"));
        Assert.Equal("nowrap", Declaration(money, "white-space"));
        Assert.Contains(".pos-cockpit-actions.is-paid #btnFinalizeCart", style, StringComparison.Ordinal);
        Assert.Contains(".pos-cockpit-actions.is-change #btnFinalizeCart", style, StringComparison.Ordinal);
        Assert.NotEmpty(BaseRule(style, ".pos-prime #posSummaryActionGrid #btnOpenPayment:disabled"));
        Assert.NotEmpty(BaseRule(style, ".pos-prime #posSummaryActionGrid #btnFinalizeCart:disabled"));
    }

    [Fact]
    public void Context_autocomplete_and_short_viewport_should_keep_safe_accessible_overflow()
    {
        var style = Read(StylePath);
        Assert.Equal("auto", Declaration(BaseRule(style, ".pos-prime .pos-prime-context__body"), "overflow"));
        Assert.Equal("auto", Declaration(BaseRule(style, ".pos-prime .pos-autocomplete-menu"), "overflow"));
        Assert.Equal("visible", Declaration(BaseRule(style, ".pos-prime .pos-prime-cart__body .pos-table-wrap"), "overflow"));
        var shortViewport = BracedBlock(style,
            "@media (min-width: 1200px) and (max-height: 639.98px)", out _);
        Assert.Contains("height: auto", shortViewport, StringComparison.Ordinal);
        Assert.Contains("overflow: visible", shortViewport, StringComparison.Ordinal);
        Assert.NotEmpty(BaseRule(style, ".pos-prime .pos-line-actions-menu"));
        Assert.Contains(":focus-visible", style, StringComparison.Ordinal);
    }

    [Fact]
    public void P2_fallback_should_remain_available_when_Prime_adapter_is_unavailable()
    {
        var style = Read(StylePath);
        var tablet = BracedBlock(style, "@media (max-width: 1199.98px)", out _);
        Assert.Contains("\"sale checkout\" \"context context\"", tablet, StringComparison.Ordinal);
        var narrow = BracedBlock(style, "@media (max-width: 991.98px)", out _);
        Assert.Contains("\"sale\" \"context\" \"checkout\"", narrow, StringComparison.Ordinal);
        var reduced = BracedBlock(style, "@media (prefers-reduced-motion: reduce)", out _);
        Assert.Contains("transition: none", reduced, StringComparison.Ordinal);
        Assert.Contains("animation: none", reduced, StringComparison.Ordinal);
    }


    [Fact]
    public void Enhanced_tablet_and_phone_rules_should_be_gated_by_successful_adapter_mount()
    {
        var style = Read(StylePath);
        var marker = style.IndexOf("20. V3-P3", StringComparison.Ordinal);
        Assert.True(marker >= 0);
        var enhanced = style[marker..];
        Assert.Contains("[data-prime-ui=\"ready\"]", enhanced, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 1199.98px)", enhanced, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 767.98px)", enhanced, StringComparison.Ordinal);
        Assert.Contains("\"sale\" \"dock\"", enhanced, StringComparison.Ordinal);
        Assert.Contains(".pos-prime-mobile-dock", enhanced, StringComparison.Ordinal);
        Assert.Contains("env(safe-area-inset-bottom)", enhanced, StringComparison.Ordinal);
    }

    [Fact]
    public void Prime_sheets_should_use_hosts_and_never_render_a_second_business_partial()
    {
        var shell = Read("GaoApp.Web/Areas/Admin/Views/POS/_POSPrimeShell.cshtml");
        foreach (var partial in new[] { "_Toolbar", "_CartTable", "_CustomerBox", "_DraftMetaPanel", "_SummaryPanel", "_SummaryActionsPanel" })
            Assert.Equal(1, Regex.Matches(shell, "<partial\\s+name=\"" + partial + "\"", RegexOptions.CultureInvariant).Count);
        foreach (var id in new[]
        {
            "posPrimeContextContent", "posPrimeContextSheet", "posPrimeContextSheetHost",
            "posPrimeContextLauncher", "posPrimeSummaryContent", "posPrimeActionContent",
            "posPrimeMoneySheet", "posPrimeMoneyLauncher", "posPrimeMoneySheetHost",
            "posPrimeMoneyActions", "posPrimeDockActions", "posPrimeDockBalance"
        })
            Assert.Equal(1, Regex.Matches(shell, "id=\"" + id + "\"", RegexOptions.CultureInvariant).Count);
        Assert.DoesNotContain("id=\"paymentModal\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"btnAddPayment\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("<form", shell, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mobile_payment_should_use_existing_modal_and_keep_original_submit_in_visible_footer()
    {
        var style = Read(StylePath);
        var script = Read("GaoApp.Web/wwwroot/Admin/js/pos/pos.prime.js");
        Assert.Contains("#paymentModal .pos-payment-workspace__footer", style, StringComparison.Ordinal);
        Assert.Contains("#paymentModal #btnAddPayment", style, StringComparison.Ordinal);
        Assert.Contains("place(elements.addPayment, submitHost)", script, StringComparison.Ordinal);
        Assert.Contains("restore(elements.addPayment)", script, StringComparison.Ordinal);
        Assert.Contains("window.visualViewport", script, StringComparison.Ordinal);
        Assert.Contains("--pos-prime-visible-height", style, StringComparison.Ordinal);
        Assert.Contains("viewport.scale", script, StringComparison.Ordinal);
        Assert.Contains("requestAnimationFrame", script, StringComparison.Ordinal);
        Assert.Contains("holdHint.textContent = 'Ctrl + Enter để xác nhận'", script, StringComparison.Ordinal);
        Assert.Contains("if (event.target === holdModal) focus(holdNote)", script, StringComparison.Ordinal);
        Assert.Contains("event.key !== 'Enter' || !event.ctrlKey", script, StringComparison.Ordinal);
        Assert.Contains("confirmHold.click()", script, StringComparison.Ordinal);
        Assert.NotEmpty(BaseRule(style, ".pos-prime-layout #holdModal #txtHoldNote:focus"));
        Assert.Equal("560px", Declaration(BaseRule(style, ".pos-prime-layout #holdModal .modal-dialog"), "max-width"));
        Assert.DoesNotContain("user-scalable=no", Read(IndexPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mobile_touch_targets_and_short_viewport_escape_should_be_explicit()
    {
        var style = Read(StylePath);
        var enhanced = style[style.IndexOf("20. V3-P3", StringComparison.Ordinal)..];
        Assert.Contains("min-height: 44px", enhanced, StringComparison.Ordinal);
        Assert.Contains("min-height: 48px", enhanced, StringComparison.Ordinal);
        Assert.Contains("font-size: 16px", enhanced, StringComparison.Ordinal);
        Assert.Contains("[data-prime-room=\"short\"]", enhanced, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto", enhanced, StringComparison.Ordinal);
        Assert.Contains("overscroll-behavior: contain", enhanced, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_feedback_should_be_under_the_shared_toolbar_with_accessible_history()
    {
        var toolbar = Read("GaoApp.Web/Areas/Admin/Views/POS/_Toolbar.cshtml");
        var view = Read("GaoApp.Web/Areas/Admin/Views/POS/_ScanFeedback.cshtml");
        var style = Read("GaoApp.Web/wwwroot/Admin/css/pos/pos.scan-feedback.css");
        Assert.True(toolbar.IndexOf("_ScanFeedback", StringComparison.Ordinal) > toolbar.IndexOf("txtBarcode", StringComparison.Ordinal));
        Assert.Contains("aria-live=\"polite\"", view, StringComparison.Ordinal);
        Assert.Contains("aria-atomic=\"true\"", view, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"posScanHistory\"", view, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap:anywhere", style, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion:reduce", style, StringComparison.Ordinal);
        Assert.DoesNotContain("position:fixed", BaseRule(style, ".pos-scan-feedback"), StringComparison.Ordinal);
    }

    [Fact]
    public void P4_held_affordance_should_keep_touch_size_and_readable_source_count()
    {
        var style = Read(StylePath);
        var rule = BaseRule(style, ".pos-prime .pos-prime-header #btnOpenHeldOrders[data-prime-held]");
        Assert.Equal("44px", Declaration(rule, "min-height"));
        Assert.Contains("[data-prime-held=\"pending\"]", style, StringComparison.Ordinal);
        Assert.Contains("[data-prime-held=\"empty\"]", style, StringComparison.Ordinal);
        Assert.NotEmpty(BaseRule(style, ".pos-prime .pos-prime-header #btnOpenHeldOrders[data-prime-held]:focus-visible"));
    }

    [Fact]
    public void P4_exception_emphasis_should_target_only_existing_renderer_classes()
    {
        var style = Read(StylePath);
        var renderer = Read("GaoApp.Web/wwwroot/Admin/js/pos/pos.render.js");

        // Bốn trạng thái này thuộc renderer; active row do PosOrder quản lý.
        foreach (var state in new[]
        {
            "has-line-discount",
            "has-promotion",
            "is-promotion-gift",
            "pos-line-just-added"
        })
        {
            Assert.Contains(state, renderer, StringComparison.Ordinal);
            Assert.Contains(state, style, StringComparison.Ordinal);
        }

        // Kiểm đúng hàm sở hữu active row, không tìm gộp trong mọi module.
        var order = WithoutComments(Read("GaoApp.Web/wwwroot/Admin/js/pos/pos.order.js"));
        var activeDeclarations = Regex.Matches(
            order,
            @"(?m)^[ \t]*function\s+renderActiveLineState\s*\(\s*\)\s*\{",
            RegexOptions.CultureInvariant);
        var activeDeclaration = Assert.Single(activeDeclarations.Cast<Match>());
        var activeState = BracedBlock(order, activeDeclaration.Value.Trim(), out _);

        Assert.Matches(@"(?m)^\s*const\s+rows\s*=\s*getLineRows\s*\(\s*\)\s*;", activeState);
        Assert.Matches(@"(?m)^\s*rows\.forEach\s*\(\s*function\s*\(\s*row\s*\)\s*\{", activeState);
        Assert.Matches(
            @"(?m)^\s*const\s+rowLineId\s*=\s*Number\s*\(\s*row\.getAttribute\s*\(\s*['""]data-line-id['""]\s*\)\s*\|\|\s*['""]0['""]\s*\)\s*;",
            activeState);
        Assert.Matches(
            @"(?m)^\s*row\.classList\.toggle\s*\(\s*['""]is-active-line['""]\s*,\s*rowLineId\s*===\s*activeLineId\s*\)\s*;",
            activeState);

        Assert.Contains("is-active-line", style, StringComparison.Ordinal);

        // Giữ CSS coverage đủ 5 trạng thái bằng rule thật, không chỉ comment.
        // Active row phải được style cả desktop và mobile.
        foreach (var selector in new[]
        {
            ".pos-prime .pos-line-row.has-line-discount:not(.is-promotion-gift) .pos-line-adjustment-note",
            ".pos-prime .pos-line-row.has-promotion:not(.is-promotion-gift) .pos-line-promo-badge",
            ".pos-prime .pos-line-table tbody tr.is-promotion-gift",
            ".pos-prime .pos-line-table tbody tr.pos-line-just-added:not(.is-active-line)",
            ".pos-prime .pos-line-table tbody tr.is-active-line",
            ".pos-prime-layout[data-prime-ui=\"ready\"] .pos-prime-mobile-table tr.is-active-line"
        })
        {
            var rule = BaseRule(style, selector);
            Assert.False(string.IsNullOrWhiteSpace(Declaration(rule, "background")),
                $"Missing visible state background: {selector}");
        }

        var p4 = style[style.IndexOf("21. V3-P4", StringComparison.Ordinal)..];
        Assert.DoesNotContain("thiếu tồn", p4, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("price-error", p4, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@keyframes", p4, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("z-index", BaseRule(Read("GaoApp.Web/wwwroot/Admin/css/pos/pos.scan-feedback.css"), ".pos-scan-feedback"), StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
            .TrimStart('\uFEFF').Replace("\r\n", "\n").Replace("\r", "\n");

    private static string[] StyleLinks(string source)
        => Regex.Matches(source, "href=\"([^\"]+\\.css)\"", RegexOptions.CultureInvariant)
            .Cast<Match>().Select(match => match.Groups[1].Value).ToArray();

    private static string WithoutComments(string text)
        => Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline | RegexOptions.CultureInvariant);

    // A small structural helper for these controlled Razor/CSS sources;
    // braces inside quoted strings and comments do not terminate a block.
    private static string BracedBlock(string text, string marker, out int endIndex)
    {
        var markerIndex = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Missing marker: {marker}");
        var open = text.IndexOf('{', markerIndex);
        Assert.True(open >= 0, $"Missing opening brace after {marker}");
        var depth = 0;
        var quote = '\0';
        var comment = false;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (comment)
            {
                if (c == '*' && i + 1 < text.Length && text[i + 1] == '/') { comment = false; i++; }
                continue;
            }
            if (quote != '\0')
            {
                if (c == '\\') { i++; continue; }
                if (c == quote) quote = '\0';
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { comment = true; i++; continue; }
            if (c is '\'' or '"') { quote = c; continue; }
            if (c == '{') depth++;
            if (c == '}')
            {
                depth--;
                if (depth == 0) { endIndex = i + 1; return text[(open + 1)..i]; }
            }
        }
        throw new InvalidOperationException($"Unbalanced block: {marker}");
    }

    private static IEnumerable<(string Selector, string Body)> CssRules(string style)
    {
        foreach (Match match in Regex.Matches(WithoutComments(style), @"([^{}]+)\{([^{}]*)\}",
            RegexOptions.CultureInvariant))
        {
            var selector = Regex.Replace(match.Groups[1].Value.Trim(), @"\s+", " ");
            if (selector.StartsWith('@')) continue;
            yield return (selector, match.Groups[2].Value);
        }
    }

    private static string BaseRule(string style, string selector)
    {
        // First match is the base rule; media overrides occur afterwards.
        var match = CssRules(style).FirstOrDefault(rule => rule.Selector.Split(',')
            .Any(part => part.Trim().Equals(selector, StringComparison.Ordinal)));
        Assert.False(string.IsNullOrWhiteSpace(match.Body), $"Missing CSS rule: {selector}");
        return match.Body!;
    }

    private static string Declaration(string ruleBody, string name)
    {
        var match = Regex.Match(ruleBody, @"(?:^|;)\s*" + Regex.Escape(name) + @"\s*:\s*([^;]+);?",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Missing declaration: {name}");
        return match.Groups[1].Value.Trim();
    }

    private static string FindRepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("GAOAPP_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(Path.Combine(configured, "GaoApp.sln")))
            return configured;
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "GaoApp.sln"))) return directory.FullName;
            }
        }
        throw new InvalidOperationException("Could not locate GaoApp repository root.");
    }
}
