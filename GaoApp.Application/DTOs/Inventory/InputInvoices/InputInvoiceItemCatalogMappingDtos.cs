namespace GaoApp.Application.DTOs.Inventory.InputInvoices;

public enum InputInvoiceItemCatalogResolutionState
{
    Unmapped = 0,
    NeedsConfirmation = 1,
    Confirmed = 2
}

public sealed class InputInvoiceItemCatalogResolutionDto
{
    public int InputInvoiceDetailId { get; set; }
    public int? MappingId { get; set; }
    public InputInvoiceItemCatalogResolutionState State { get; set; }
    public string StateName => State.ToString();
    public string? ReasonCode { get; set; }
    public string? Message { get; set; }
    public string? SupplierItemCode { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? UnitName { get; set; }
    public decimal XmlQuantity { get; set; }
    public int? ProductVariantId { get; set; }
    public string? ProductName { get; set; }
    public string? VariantSku { get; set; }
    public int? ProductUnitConversionId { get; set; }
    public int? ConfirmedUnitId { get; set; }
    public string? ConfirmedUnitName { get; set; }
    public decimal? ConfirmedFactor { get; set; }
    public int? ConfirmedBaseUnitId { get; set; }
    public string? ConfirmedBaseUnitName { get; set; }
    public bool IsDefaultForSale { get; set; }
    public string? MappingRowVersion { get; set; }
    public bool IsAutoApplicable => State == InputInvoiceItemCatalogResolutionState.Confirmed;
    public decimal? DerivedBaseQuantity =>
        ConfirmedFactor is > 0m ? XmlQuantity * ConfirmedFactor.Value : null;
}
