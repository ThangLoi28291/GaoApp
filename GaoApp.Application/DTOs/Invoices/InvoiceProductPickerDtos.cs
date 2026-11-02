namespace GaoApp.Application.DTOs.Invoices;

public sealed class InvoiceProductPickerPageDto
{
    public bool StockFilterApplied { get; init; }
    public int? WarehouseId { get; init; }
    public string? WarehouseName { get; init; }
    public DateTime CheckedAtUtc { get; init; } = DateTime.UtcNow;
    public IReadOnlyList<InvoiceProductPickerItemDto> Items { get; init; } = [];
}

public sealed class InvoiceProductPickerItemDto : InvoiceProductVariantSearchItemDto
{
    public int? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public decimal? AvailableQuantity { get; set; }
    public decimal? AlreadyOnInvoiceQuantity { get; set; }
    public decimal? MaximumAdditionalQuantity { get; set; }
}
