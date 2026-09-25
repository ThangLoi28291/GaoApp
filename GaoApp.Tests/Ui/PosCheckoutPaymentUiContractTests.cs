using System.Text.RegularExpressions;
using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class PosCheckoutPaymentUiContractTests
{
    private static readonly string RepositoryRoot =
        FindRepositoryRoot();

    private static readonly string IndexPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "Areas",
            "Admin",
            "Views",
            "POS",
            "Index.cshtml");

    private static readonly string PaymentScriptPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "wwwroot",
            "Admin",
            "js",
            "pos",
            "pos.payment.js");

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
    public void Payment_workspace_should_preserve_canonical_DOM_ids_exactly_once()
    {
        var markup =
            File.ReadAllText(IndexPath);

        markup = Regex.Replace(
            markup,
            @"<!--.*?-->",
            string.Empty,
            RegexOptions.Singleline);

        var requiredIds = new[]
        {
            "paymentModal",
            "posPaymentRetryState",
            "paySumSubtotal",
            "paySumDiscount",
            "paySumGrandTotal",
            "paySumPaid",
            "paySumBalance",
            "paySumChange",
            "paymentForm",
            "payMethod",
            "payAmount",
            "btnPayExact",
            "payExactAmount",
            "paymentQuickAmounts",
            "payPreviewBox",
            "payPreviewStateText",
            "payPreviewBalance",
            "payPreviewChange",
            "paymentQrBox",
            "paymentQrStatusText",
            "btnOpenPaymentQrPopup",
            "payReference",
            "payProvider",
            "btnAddPayment",
            "paymentModalListBox",
            "paymentInlineErrorBox",
            "payFooterStateText",
            "btnFinalizeFromPaymentModal",
            "paymentQrModal",
            "paymentQrContent",
            "paymentQrImage",
            "paymentQrBankName",
            "paymentQrAccountNumber",
            "paymentQrAccountName",
            "paymentQrAmount",
            "paymentQrTransferContent",
            "btnConfirmPaymentQrPaid",
            "btnCancelPaymentQr"
        };

        foreach (var id in requiredIds)
        {
            var count =
                CountOccurrences(
                    markup,
                    $"id=\"{id}\"");

            Assert.True(
                count == 1,
                $"Expected DOM id '{id}' exactly once, but found {count} occurrence(s).");
        }
    }

    [Fact]
    public void Payment_workspace_should_use_segmented_methods_and_quick_cash()
    {
        var index =
            File.ReadAllText(IndexPath);

        Assert.Contains(
            "pos-payment-workspace-dialog",
            index,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-pay-method-value=\"0\"",
            index,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-pay-method-value=\"1\"",
            index,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-pay-method-value=\"2\"",
            index,
            StringComparison.Ordinal);

        Assert.Contains(
            "id=\"paymentQuickAmounts\"",
            index,
            StringComparison.Ordinal);

        Assert.Contains(
            "id=\"btnPayExact\"",
            index,
            StringComparison.Ordinal);

        Assert.Contains(
            "id=\"paymentQrContent\"",
            index,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Payment_script_should_add_only_presentation_helpers_around_existing_engine()
    {
        var script =
            File.ReadAllText(
                PaymentScriptPath);

        Assert.Contains(
            "function syncPaymentMethodButtons",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "function buildQuickCashCandidates",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "function renderQuickCashButtons",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "function setPaymentWorkspaceOpen",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-pay-method-value",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-pay-amount",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_payment_and_finalize_endpoints_should_remain_single()
    {
        var script =
            File.ReadAllText(
                PaymentScriptPath);

        var normalizedScript =
            Regex.Replace(
                script,
                @"\s+",
                " ");

        Assert.Equal(
            1,
            CountOccurrences(
                script,
                "/admin/pos/cart/current/payments"));

        Assert.Equal(
            1,
            CountOccurrences(
                script,
                "/admin/pos/${targetOrderId}/finalize"));

        Assert.Equal(
            1,
            CountOccurrences(
                script,
                "/manual-confirm"));

        Assert.Equal(
            1,
            CountOccurrences(
                script,
                "/admin/pos/cart/current/payment-qr"));

        Assert.Contains(
            "if (method === 0)",
            normalizedScript,
            StringComparison.Ordinal);

        Assert.Contains(
            "await finalizeRecordedPayment(draft.orderId);",
            normalizedScript,
            StringComparison.Ordinal);

        Assert.Contains(
            "options?.finalizeWhenPaid === true",
            normalizedScript,
            StringComparison.Ordinal);
    }

    [Fact]
    public void P3_styles_should_be_scoped_without_new_important_rules()
    {
        var css = LegacyPosCssContractR1.Parse(File.ReadAllText(StylePath));

        // Giữ scope của rule thật cho các surface Payment/QR; không tìm marker comment.
        css.Require(".pos-payment-workspace-modal .pos-payment-workspace-dialog", "display", "flex");
        css.RequireDeclaration(".pos-payment-workspace-modal .pos-payment-workspace-dialog", "max-width");
        css.Require(".pos-payment-method-button", "display", "flex");
        css.RequireDeclaration(".pos-payment-quick-button", "min-height");
        css.RequireDeclaration(".pos-payment-ledger-workspace", "border-top");
        css.RequireDeclaration(".pos-payment-qr-workspace", "border-radius");
    }

    private static int CountOccurrences(
        string source,
        string value)
    {
        var count = 0;
        var index = 0;

        while (true)
        {
            index = source.IndexOf(
                value,
                index,
                StringComparison.Ordinal);

            if (index < 0)
                return count;

            count++;
            index += value.Length;
        }
    }
    [Fact]
    public void Finalize_presentation_should_follow_persisted_draft_not_typed_preview()
    {
        var script =
            File.ReadAllText(
                PaymentScriptPath);

        Assert.Contains(
            "function syncFinalizePresentationFromDraft",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "const actualBalance",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "getDraftBalance(draft)",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "btnFinalizeFromPaymentModal.disabled",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "is-payment-finalize-ready",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "Ghi nhận thanh toán để cập nhật số tiền thực tế.",
            script,
            StringComparison.Ordinal);

        Assert.Equal(
            1,
            CountOccurrences(
                script,
                "btnFinalizeFromPaymentModal.disabled ="));

        Assert.Equal(
            1,
            CountOccurrences(
                script,
                "payFooterStateText.textContent ="));
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
