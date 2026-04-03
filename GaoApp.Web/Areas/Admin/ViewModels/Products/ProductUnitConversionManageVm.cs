namespace GaoApp.Web.Areas.Admin.ViewModels.Products;

/// <summary>
/// ViewModel dùng để render modal quản lý quy đổi đơn vị + barcode.
/// </summary>
public class ProductUnitConversionManageVm
{
    public List<UnitOptionVm> Units { get; set; } = new();
    public List<BarcodeTypeOptionVm> BarcodeTypes { get; set; } = new();
}

public class UnitOptionVm
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
}

public class BarcodeTypeOptionVm
{
    public int Value { get; set; }
    public string Name { get; set; } = default!;
}