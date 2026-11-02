namespace GaoApp.Application.DTOs.Inventory;

public sealed class DeleteStockDocumentLineRequest
{
    public string? RowVersion { get; set; }
    public string? LineRowVersion { get; set; }
}
