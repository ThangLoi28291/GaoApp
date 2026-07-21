namespace GaoApp.Application.DTOs.Inventory.Warehouse;

public class WarehouseDto
{
    public int Id { get; set; }
    public int LegalEntityId { get; set; }
    public string LegalEntityName { get; set; } = string.Empty;
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Location { get; set; }
    public string? Note { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public bool AllowNegativeInventory { get; set; }
}
