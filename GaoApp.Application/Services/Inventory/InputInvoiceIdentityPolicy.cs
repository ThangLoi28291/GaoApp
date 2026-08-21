using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Inventory;

public static class InputInvoiceIdentityPolicy
{
    public static void ApplyRequiredIdentity(InputInvoiceHead invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        invoice.SellerTaxCode = NormalizeDisplay(invoice.SellerTaxCode);
        invoice.InvoiceSeries = NormalizeDisplay(invoice.InvoiceSeries);
        invoice.InvoiceNumber = NormalizeDisplay(invoice.InvoiceNumber);

        invoice.NormalizedSellerTaxCode = NormalizeTaxCode(
            invoice.SellerTaxCode);
        invoice.NormalizedInvoiceSeries = NormalizeIdentityToken(
            invoice.InvoiceSeries);
        invoice.NormalizedInvoiceNumber = NormalizeIdentityToken(
            invoice.InvoiceNumber);
        invoice.InvoiceIdentityDate = invoice.InvoiceDate?.Date;

        if (string.IsNullOrEmpty(invoice.NormalizedSellerTaxCode))
            throw new BusinessRuleException(
                "XML thiếu mã số thuế người bán để định danh hóa đơn.");

        if (string.IsNullOrEmpty(invoice.NormalizedInvoiceSeries))
            throw new BusinessRuleException(
                "XML thiếu ký hiệu hóa đơn để định danh hóa đơn.");

        if (string.IsNullOrEmpty(invoice.NormalizedInvoiceNumber))
            throw new BusinessRuleException(
                "XML thiếu số hóa đơn để định danh hóa đơn.");

        if (!invoice.InvoiceIdentityDate.HasValue)
            throw new BusinessRuleException(
                "XML thiếu ngày lập hóa đơn hợp lệ để định danh hóa đơn.");
    }

    public static string? NormalizeTaxCode(string? value)
    {
        var normalized = NormalizeText(value);
        if (normalized.Length == 0)
            return null;

        normalized = normalized
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("\t", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        return normalized.Length == 0 ? null : normalized;
    }

    public static string? NormalizeIdentityToken(string? value)
    {
        var normalized = NormalizeText(value);
        if (normalized.Length == 0)
            return null;

        normalized = normalized
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\t", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? NormalizeDisplay(string? value)
    {
        var normalized = NormalizeText(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private static string NormalizeText(string? value)
        => (value ?? string.Empty).Trim();
}
