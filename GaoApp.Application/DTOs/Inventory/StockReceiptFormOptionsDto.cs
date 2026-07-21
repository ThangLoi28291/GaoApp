namespace GaoApp.Application.DTOs.Inventory;

public sealed class StockReceiptFormOptionsDto
{
    public int DefaultLegalEntityId { get; set; }
    public List<StockReceiptLegalEntityOptionDto> LegalEntities { get; set; } = new();
    public List<StockReceiptWarehouseOptionDto> Warehouses { get; set; } = new();
    public List<StockReceiptTaxOptionDto> Taxes { get; set; } = new();
}

public sealed class StockReceiptTaxOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Rate { get; set; }
}

public sealed class StockReceiptLegalEntityOptionDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? DefaultWarehouseId { get; set; }
    public bool IsDefaultForPurchase { get; set; }
}

public sealed class StockReceiptWarehouseOptionDto
{
    public int Id { get; set; }
    public int LegalEntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
