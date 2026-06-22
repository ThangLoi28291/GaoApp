using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

internal static class InvoiceDetailFactory
{
    public static InvoiceDetail FromOrderLine(
        InvoiceHead invoiceHead,
        OrderLine line)
    {
        var quantity = line.Quantity;
        var unitPrice = line.UnitPrice;
        var amount = quantity * unitPrice;

        return new InvoiceDetail
        {
            InvoiceHeadId = invoiceHead.Id,

            OrderLineId = line.Id,
            ProductVariantId = line.VariantId,

            SourceType = InvoiceDetailSourceType.FromOrderLine,

            ItemName = line.ItemName,
            UnitName = line.UnitName,

            Quantity = quantity,
            UnitPrice = unitPrice,
            Amount = amount,

            VatRate = 0,
            VatAmount = 0,
            TotalAmount = amount,

            Note = "Tự sinh từ POS OrderLine có ProductVariant.HasInputInvoice = true."
        };
    }

    public static InvoiceDetail Manual(
        InvoiceHead invoiceHead,
        CreateManualInvoiceDetailRequest request)
    {
        var detail = new InvoiceDetail
        {
            InvoiceHeadId = invoiceHead.Id,
            OrderLineId = null,
            ProductVariantId = request.ProductVariantId,
            SourceType = InvoiceDetailSourceType.Manual,

            ItemName = request.ItemName.Trim(),
            UnitName = string.IsNullOrWhiteSpace(request.UnitName)
                ? null
                : request.UnitName.Trim(),

            Quantity = request.Quantity,
            UnitPrice = request.UnitPrice,
            VatRate = request.VatRate,

            Note = string.IsNullOrWhiteSpace(request.Note)
                ? null
                : request.Note.Trim()
        };

        InvoiceAmountCalculator.RecalculateDetail(detail);

        return detail;
    }
}