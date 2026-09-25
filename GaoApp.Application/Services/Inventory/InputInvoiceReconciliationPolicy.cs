using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public static class InputInvoiceReconciliationPolicy
{
    public const decimal QuantityTolerance = 0.0001m;
    public const decimal MoneyTolerance = 1m;

    public static bool QuantityMatches(decimal difference)
        => Math.Abs(difference) <= QuantityTolerance;

    public static bool MoneyMatches(decimal difference)
        => Math.Abs(difference) <= MoneyTolerance;

    public static InputInvoiceDetailReconciliationState ResolveDetailState(
        bool complete, bool ignored, bool vatComparable,
        decimal quantityDifference, decimal baseUnitPriceDifference,
        decimal vatAmountDifference, decimal? receiptVatRate, decimal? xmlVatRate)
    {
        if (ignored) return InputInvoiceDetailReconciliationState.Ignored;
        if (!complete) return InputInvoiceDetailReconciliationState.Incomplete;
        if (!vatComparable) return InputInvoiceDetailReconciliationState.NeedsReview;
        var quantityMismatch = !QuantityMatches(quantityDifference);
        var amountMismatch = !MoneyMatches(baseUnitPriceDifference);
        var vatMismatch = receiptVatRate != xmlVatRate || !MoneyMatches(vatAmountDifference);
        var count = (quantityMismatch ? 1 : 0) + (amountMismatch ? 1 : 0) +
            (vatMismatch ? 1 : 0);
        if (count > 1) return InputInvoiceDetailReconciliationState.CombinedMismatch;
        if (quantityMismatch) return InputInvoiceDetailReconciliationState.QuantityMismatch;
        if (amountMismatch) return InputInvoiceDetailReconciliationState.AmountMismatch;
        if (vatMismatch) return InputInvoiceDetailReconciliationState.VatMismatch;
        return InputInvoiceDetailReconciliationState.Matched;
    }

    public static string Fingerprint(InputInvoiceReconciliationDto value)
    {
        var builder = new StringBuilder();
        Append(builder, value.StockDocumentId);
        Append(builder, value.InputInvoiceHeadId);
        if (value.Header is not null)
        {
            Append(builder, value.Header.ReceiptSubtotalBeforeVat);
            Append(builder, value.Header.XmlTotalBeforeTax);
            Append(builder, value.Header.ReceiptVatAmount);
            Append(builder, value.Header.XmlTaxAmount);
            Append(builder, value.Header.ReceiptGoodsTotal);
            Append(builder, value.Header.XmlPaymentAmount);
            Append(builder, value.Header.NeedsReview);
            Append(builder, value.Header.Reason);
        }
        foreach (var detail in value.Details.OrderBy(x => x.InputInvoiceDetailId))
        {
            Append(builder, detail.InputInvoiceDetailId);
            Append(builder, (int)detail.State);
            Append(builder, detail.XmlQuantity);
            Append(builder, detail.DerivedBaseQuantity);
            Append(builder, detail.ReceiptBaseQuantity);
            Append(builder, detail.MappingId);
            Append(builder, detail.ProductVariantId);
            Append(builder, detail.ProductUnitConversionId);
            Append(builder, detail.ConfirmedUnitId);
            Append(builder, detail.ConfirmedFactor);
            Append(builder, detail.ConfirmedBaseUnitId);
            Append(builder, detail.ReceiptBeforeVatAmount);
            Append(builder, detail.XmlBeforeVatAmount);
            Append(builder, detail.ReceiptBaseUnitPriceBeforeVat);
            Append(builder, detail.XmlBaseUnitPriceBeforeVat);
            Append(builder, detail.BaseUnitPriceDifference);
            Append(builder, detail.ReceiptVatRate);
            Append(builder, detail.XmlVatRate);
            Append(builder, detail.ReceiptVatAmount);
            Append(builder, detail.XmlVatAmount);
            Append(builder, detail.IsIgnored);
            Append(builder, detail.IgnoreReason);
            foreach (var lineId in detail.StockDocumentLineIds.Order()) Append(builder, lineId);
        }
        foreach (var line in value.ExcludedLines.OrderBy(x => x.StockDocumentLineId))
        {
            Append(builder, line.StockDocumentLineId);
            Append(builder, line.Reason);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, object? value)
    {
        var text = value switch
        {
            null => "~",
            decimal number => number.ToString("0.############################", CultureInfo.InvariantCulture),
            DateTime time => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
        builder.Append(text.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':').Append(text).Append('|');
    }
}
