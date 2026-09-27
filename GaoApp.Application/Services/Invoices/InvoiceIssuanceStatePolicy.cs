using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public static class InvoiceIssuanceStatePolicy
{
    public static bool IsIssuedLike(InvoiceHead invoice)
    {
        if (!string.IsNullOrWhiteSpace(invoice.ProviderInvoiceNo))
            return true;

        return invoice.ProviderStatus is
            InvoiceProviderStatus.Issued or
            InvoiceProviderStatus.PdfDownloaded or
            InvoiceProviderStatus.ZipDownloaded or
            InvoiceProviderStatus.EmailSent;
    }

    public static bool IsProviderUncertain(InvoiceHead invoice)
    {
        if (invoice.ProviderStatus is
            InvoiceProviderStatus.Issuing or
            InvoiceProviderStatus.IssuedWaitingNumber)
        {
            return true;
        }

        var code = invoice.LastErrorCode ?? string.Empty;
        var message = invoice.LastErrorMessage ?? string.Empty;

        if (code.Equals(
                "TIMEOUT",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (code.Equals(
                "UNKNOWN_RESULT",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (code.Equals(
                "HTTP_500",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (code.Equals(
                "VIETTEL_SERVER_500",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (code.StartsWith(
                "HTTP_5",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (message.Contains(
                "timeout",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (message.Contains(
                "HTTP 500",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Chỉ những trạng thái chắc chắn chưa bước vào legal/provider issuance
    /// mới cho Manual <-> Automatic.
    /// </summary>
    public static bool CanChangeRoute(InvoiceHead invoice)
    {
        if (invoice.IsLocked)
            return false;

        if (IsIssuedLike(invoice))
            return false;

        if (IsProviderUncertain(invoice))
            return false;

        return invoice.ProviderStatus is
            InvoiceProviderStatus.LocalDraft or
            InvoiceProviderStatus.ReadyToIssue or
            InvoiceProviderStatus.Previewed;
    }

    /// <summary>
    /// QR chỉ được chỉnh buyer khi invoice vẫn ở trạng thái draft an toàn.
    /// </summary>
    public static bool CanEditBuyerInformation(InvoiceHead invoice)
        => CanChangeRoute(invoice);
}