using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Invoices;

internal static class InvoiceAmountCalculator
{
    public static void RecalculateDetail(InvoiceDetail detail)
    {
        detail.Amount = Math.Round(
            detail.Quantity * detail.UnitPrice,
            2,
            MidpointRounding.AwayFromZero);

        detail.VatAmount = Math.Round(
            detail.Amount * detail.VatRate / 100m,
            2,
            MidpointRounding.AwayFromZero);

        detail.TotalAmount = detail.Amount + detail.VatAmount;
    }

    public static void RecalculateHead(InvoiceHead invoiceHead)
    {
        var activeDetails = invoiceHead.Details
            .Where(x => !x.IsDeleted)
            .ToList();

        invoiceHead.TotalQuantity = activeDetails.Sum(x => x.Quantity);
        invoiceHead.SubTotal = activeDetails.Sum(x => x.Amount);
        invoiceHead.VatAmount = activeDetails.Sum(x => x.VatAmount);
        invoiceHead.GrandTotal = activeDetails.Sum(x => x.TotalAmount);
    }
}