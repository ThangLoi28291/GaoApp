using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Web.Areas.Admin.Models.Inventory;

public sealed class StockDocumentLinesTableViewModel
{
    public int DocumentId { get; set; }
    public string RowVersion { get; set; } = "";
    public List<StockDocumentLineDto> Lines { get; set; } = new();
    public List<GaoApp.Application.DTOs.Purchases.StockDocumentProvisionalItemDto> ProvisionalItems { get; set; } = new();
    public bool CanEdit { get; set; }
    public bool CanViewCost { get; set; }
}
