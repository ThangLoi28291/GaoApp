using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

internal static class InvoiceDetailFactory
{
    public static InvoiceDetail FromAllocation(
        InvoiceHead invoiceHead,
        OrderLegalEntityAllocation allocation,
        OrderLine line)
    {
        // NetAmount là phần doanh thu cuối cùng đã phân bổ đủ line/combo/order/voucher
        // discount. Dùng giá bán hiệu dụng để mỗi InvoiceHead chỉ mang đúng phần tiền
        // của HKD, không nhân đôi GrandTotal của đơn gộp.
        var effectiveUnitPrice = allocation.Quantity == 0m
            ? 0m
            : Math.Round(
                allocation.NetAmount / allocation.Quantity,
                2,
                MidpointRounding.AwayFromZero);

        return new InvoiceDetail
        {
            StoreId = invoiceHead.StoreId,
            InvoiceHeadId = invoiceHead.Id,
            OrderLineId = line.Id,
            OrderLegalEntityAllocationId = allocation.Id,
            ProductVariantId = allocation.ProductVariantId,
            SourceType = InvoiceDetailSourceType.FromOrderLine,
            ItemName = line.ItemName,
            UnitName = line.UnitName,
            Quantity = allocation.Quantity,
            UnitPrice = effectiveUnitPrice,
            Amount = allocation.NetAmount,
            VatRate = 0,
            VatAmount = 0,
            TotalAmount = allocation.NetAmount,
            Note = $"Tự sinh từ allocation #{allocation.Id}; " +
                $"LegalEntityId={allocation.LegalEntityId}; WarehouseId={allocation.WarehouseId}."
        };
    }

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
