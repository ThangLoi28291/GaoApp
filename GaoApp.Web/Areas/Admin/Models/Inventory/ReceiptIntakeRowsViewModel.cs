using GaoApp.Application.DTOs.Purchases;

namespace GaoApp.Web.Areas.Admin.Models.Inventory;

public sealed class ReceiptIntakeRowsViewModel
{
    public IEnumerable<StockDocumentProvisionalItemDto> Items { get; set; } = [];
    public bool CanEdit { get; set; }
    public bool Compact { get; set; }
    public bool CanViewCost { get; set; }
    public bool HasInvoiceColumn { get; set; }
    public int LineOffset { get; set; }
}
