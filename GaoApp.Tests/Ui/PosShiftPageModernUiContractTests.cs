using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class PosShiftPageModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "POSShiftPage", "Index.cshtml");

    private static readonly string StylePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "css", "pos", "pos-shift.css");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "js", "pos", "pos.shift.page.js");

    [Fact]
    public void Shift_page_should_keep_all_canonical_runtime_ids_single()
    {
        var index = File.ReadAllText(IndexPath);
        var requiredIds = new[]
        {
            "shiftStatusBadge",
            "shiftId",
            "shiftCode",
            "openedAt",
            "openingCash",
            "cashSalesTotal",
            "nonCashSalesTotal",
            "cashRefundTotal",
            "nonCashRefundTotal",
            "refundTotalShift",
            "refundCountShift",
            "cashInTotal",
            "cashOutTotal",
            "closingCashExpected",
            "openNote",
            "voidCountShift",
            "btnShowOpenModal",
            "btnShowCashTxnModal",
            "btnShowCloseModal",
            "cashTxnModal",
            "cashTxnType",
            "cashTxnAmount",
            "cashTxnReason",
            "cashTxnNote",
            "btnAddCashTxn",
            "openShiftModal",
            "btnOpenShiftConfirm",
            "closeShiftModal",
            "btnCloseShift"
        };

        foreach (var id in requiredIds)
        {
            Assert.Equal(1, CountOccurrences(index, $"id=\"{id}\""));
        }
    }

    [Fact]
    public void Cash_position_should_be_primary_and_should_not_duplicate_expected_closing_cash()
    {
        var index = File.ReadAllText(IndexPath);

        Assert.Contains("shift-summary-layout", index, StringComparison.Ordinal);
        Assert.Contains("shift-cash-position-card", index, StringComparison.Ordinal);
        Assert.Contains("shift-secondary-card", index, StringComparison.Ordinal);
        Assert.Contains("Vị trí tiền mặt trong ca", index, StringComparison.Ordinal);
        Assert.Contains("Tiền mặt dự kiến cuối ca", index, StringComparison.Ordinal);
        Assert.DoesNotContain("closingCashExpectedMirror", index, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(index, "id=\"closingCashExpected\""));
    }

    [Fact]
    public void Cash_modal_should_keep_canonical_owner_and_segmented_presentation_only()
    {
        var index = File.ReadAllText(IndexPath);
        var modal = ExtractBetween(
            index,
            "<!-- Cash Transaction Modal -->",
            "<!-- Close Shift Modal -->");

        foreach (var id in new[]
        {
            "cashTxnType",
            "cashTxnAmount",
            "cashTxnReason",
            "cashTxnNote",
            "btnAddCashTxn"
        })
        {
            Assert.Equal(1, CountOccurrences(modal, $"id=\"{id}\""));
        }

        Assert.Equal(1, CountOccurrences(modal, "data-cash-txn-type=\"1\""));
        Assert.Equal(1, CountOccurrences(modal, "data-cash-txn-type=\"2\""));
        Assert.Contains("class=\"form-select visually-hidden\"", modal, StringComparison.Ordinal);
        Assert.Contains("Bắt buộc", modal, StringComparison.Ordinal);
        Assert.Contains("Ctrl", modal, StringComparison.Ordinal);
        Assert.Contains("Enter", modal, StringComparison.Ordinal);
        Assert.DoesNotContain("/admin/pos/", modal, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fetch(", modal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Shift_page_script_should_preserve_existing_endpoint_owners_exactly_once()
    {
        var script = File.ReadAllText(ScriptPath);
        var expectedCalls = new[]
        {
            "fetchJson('/admin/api/warehouses')",
            "fetchJson('/admin/pos/shift/current')",
            "fetchJson('/admin/pos/shift/cash-transactions')",
            "postJson('/admin/pos/shift/open'",
            "postJson('/admin/pos/shift/cash-transaction'",
            "postJson('/admin/pos/shift/close'"
        };

        foreach (var call in expectedCalls)
        {
            Assert.Equal(1, CountOccurrences(script, call));
        }

        Assert.DoesNotContain("setInterval(", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Cash_submit_should_still_read_canonical_type_and_use_existing_submit_owner()
    {
        var script = File.ReadAllText(ScriptPath);
        var body = ExtractBetween(
            script,
            "async function addCashTransaction()",
            "async function closeShift()");

        Assert.Contains("withButtonLoading(btnAddCashTxn", body, StringComparison.Ordinal);
        Assert.Contains("postJson('/admin/pos/shift/cash-transaction'", body, StringComparison.Ordinal);
        Assert.Contains("type: parseInt((cashTxnType && cashTxnType.value)", body, StringComparison.Ordinal);
        Assert.Contains("amount: parseFloat((cashTxnAmount && cashTxnAmount.value)", body, StringComparison.Ordinal);
        Assert.Contains("reason: (cashTxnReason && cashTxnReason.value)", body, StringComparison.Ordinal);
        Assert.Contains("note: (cashTxnNote && cashTxnNote.value)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Cash_type_buttons_should_only_sync_canonical_value_and_focus_amount()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("const cashTxnTypeButtons = Array.from(document.querySelectorAll('[data-cash-txn-type]'))", script, StringComparison.Ordinal);
        Assert.Contains("cashTxnType.value = value.toString()", script, StringComparison.Ordinal);
        Assert.Contains("button.classList.toggle('active', isActive)", script, StringComparison.Ordinal);
        Assert.Contains("button.setAttribute('aria-pressed', isActive ? 'true' : 'false')", script, StringComparison.Ordinal);
        Assert.Contains("setCashTxnType(this.dataset.cashTxnType || '1')", script, StringComparison.Ordinal);
        Assert.Contains("cashTxnAmount.focus()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("cashTxnTypeButtons.forEach(button => {\n            button.addEventListener('click', async", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Cash_modal_should_autofocus_amount_and_keep_ctrl_enter_owner()
    {
        var script = File.ReadAllText(ScriptPath);
        var modalBinding = ExtractBetween(
            script,
            "if (cashTxnModalEl) {",
            "if (closeShiftModalEl) {");

        Assert.Contains("e.ctrlKey && e.key === 'Enter'", modalBinding, StringComparison.Ordinal);
        Assert.Contains("addCashTransaction()", modalBinding, StringComparison.Ordinal);
        Assert.Contains("cashTxnModalEl.addEventListener('shown.bs.modal'", modalBinding, StringComparison.Ordinal);
        Assert.Contains("cashTxnAmount.focus()", modalBinding, StringComparison.Ordinal);
        Assert.Contains("cashTxnAmount.select()", modalBinding, StringComparison.Ordinal);
        Assert.DoesNotContain("cashTxnType.focus()", modalBinding, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_and_close_shift_should_keep_existing_denom_first_focus_and_ctrl_enter()
    {
        var script = File.ReadAllText(ScriptPath);
        var openBinding = ExtractBetween(
            script,
            "if (openShiftModalEl) {",
            "if (cashTxnModalEl) {");
        var closeBinding = ExtractBetween(
            script,
            "if (closeShiftModalEl) {",
            "    }\n\n    // =========================\n    // Boot");

        Assert.Contains("openShift()", openBinding, StringComparison.Ordinal);
        Assert.Contains("focusFirstDenom('open')", openBinding, StringComparison.Ordinal);
        Assert.Contains("closeShift()", closeBinding, StringComparison.Ordinal);
        Assert.Contains("focusFirstDenom('close')", closeBinding, StringComparison.Ordinal);
    }

    [Fact]
    public void Expected_closing_cash_should_remain_backend_rendered_without_mirror_or_client_formula()
    {
        var script = File.ReadAllText(ScriptPath);
        var render = ExtractBetween(
            script,
            "function renderOpenShift(shift)",
            "function renderCloseShiftCompare()");
        var setter = ExtractBetween(
            script,
            "function setClosingCashExpectedText(value)",
            "function syncCashTxnTypePresentation()");

        Assert.Contains("setClosingCashExpectedText(shift.closingCashExpected)", render, StringComparison.Ordinal);
        Assert.Contains("setText(closingCashExpected, text)", setter, StringComparison.Ordinal);
        Assert.DoesNotContain("closingCashExpectedMirror", script, StringComparison.Ordinal);
        Assert.DoesNotContain("openingCash +", setter, StringComparison.Ordinal);
        Assert.DoesNotContain("cashSalesTotal +", setter, StringComparison.Ordinal);
    }

    [Fact]
    public void Shift_css_should_scope_compact_context_cash_position_and_responsive_contracts()
    {
        var style = File.ReadAllText(StylePath);

        Assert.Contains("POS SHIFT P1 — OPERATIONAL CONTROL CENTER", style, StringComparison.Ordinal);
        Assert.Contains(".pos-context-bar.pos-context-bar", style, StringComparison.Ordinal);
        Assert.Contains(".shift-command-bar", style, StringComparison.Ordinal);
        Assert.Contains(".shift-summary-layout", style, StringComparison.Ordinal);
        Assert.Contains(".shift-cash-position-card", style, StringComparison.Ordinal);
        Assert.Contains(".shift-cash-position-total__value", style, StringComparison.Ordinal);
        Assert.Contains(".shift-secondary-card", style, StringComparison.Ordinal);
        Assert.Contains(".shift-cash-type-segment", style, StringComparison.Ordinal);
        Assert.Contains(".shift-cash-type-btn.active[data-cash-txn-type=\"1\"]", style, StringComparison.Ordinal);
        Assert.Contains(".shift-cash-type-btn.active[data-cash-txn-type=\"2\"]", style, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 1199.98px)", style, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 767.98px)", style, StringComparison.Ordinal);
    }

    private static string ExtractBetween(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Start marker was not found: {startMarker}");

        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"End marker was not found after start marker: {endMarker}");

        return source[start..end];
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("GAOAPP_REPOSITORY_ROOT");

        if (!string.IsNullOrWhiteSpace(configured)
            && File.Exists(Path.Combine(configured, "GaoApp.sln")))
        {
            return configured;
        }

        foreach (var start in new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        })
        {
            var directory = new DirectoryInfo(start);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate GaoApp repository root.");
    }
}
