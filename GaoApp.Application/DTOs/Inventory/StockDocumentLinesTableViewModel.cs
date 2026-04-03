using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Web.Areas.Admin.Models.Inventory;

public class StockDocumentLinesTableViewModel
{
    public List<StockDocumentLineDto> Lines { get; set; } = new();
    public bool CanEdit { get; set; }
}