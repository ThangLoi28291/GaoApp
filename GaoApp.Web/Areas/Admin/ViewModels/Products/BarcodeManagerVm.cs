namespace GaoApp.Web.Areas.Admin.ViewModels.Products;

public sealed class BarcodeManagerVm
{
    public int ProductUnitConversionId { get; set; }

    public string? ProductName { get; set; }
    public string? VariantSku { get; set; }
    public string? UnitName { get; set; }
}