using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Purchases;

namespace GaoApp.Web.Security;

/// <summary>One server-side projection for receipt readers without commercial access.</summary>
public static class ReceiptCommercialRedaction
{
    public static void Apply(StockDocumentDto document)
    {
        document.TotalAmount = 0m;
        document.SubtotalBeforeVat = 0m;
        document.VatAmount = 0m;
        document.FreightTotal = 0m;
        foreach (var line in document.Lines)
        {
            line.UnitCost = 0m;
            line.LineTotal = 0m;
            line.TaxId = null;
            line.SuggestedTaxId = null;
            line.TaxNameSnapshot = null;
            line.UnitPriceBeforeVat = 0m;
            line.EditableUnitPriceBeforeVat = null;
            line.LastPurchaseUnitPriceBeforeVat = null;
            line.TaxRate = 0m;
            line.VatAmount = 0m;
            line.UnitPriceAfterVat = 0m;
            line.FreightAllocation = 0m;
            line.XmlLineAmount = null;
            line.AmountDifference = 0m;
        }
        Apply(document.ProvisionalItems);
    }

    public static void Apply(IEnumerable<StockDocumentProvisionalItemDto> items)
    {
        foreach (var item in items)
        {
            if (item.ReviewDraft is not { } draft) continue;
            draft.PurchasePrice = null;
            draft.RetailPrice = null;
            draft.WholesalePrice = null;
        }
    }
}
