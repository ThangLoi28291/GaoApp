using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Web.Areas.Admin.ViewModels.Products;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/barcode-manager")]
public sealed class BarcodeManagerController : Controller
{
    private readonly IProductVariantUnitBarcodeRepository _barcodeRepository;
    private readonly ICurrentStore _currentStore;

    public BarcodeManagerController(
        IProductVariantUnitBarcodeRepository barcodeRepository,
        ICurrentStore currentStore)
    {
        _barcodeRepository = barcodeRepository;
        _currentStore = currentStore;
    }

    [HttpGet("{conversionId:int}")]
    public async Task<IActionResult> Index(int conversionId, CancellationToken ct)
    {
        var storeId = _currentStore.StoreId;

        var conversion = await _barcodeRepository.GetConversionDetailAsync(storeId, conversionId, ct);
        if (conversion == null)
            return NotFound();

        var vm = new BarcodeManagerVm
        {
            ProductUnitConversionId = conversion.Id,
            ProductName = conversion.ProductVariant.Product.Name,
            VariantSku = conversion.ProductVariant.Sku,
            UnitName = conversion.Unit?.Name
        };

        ViewData["Title"] = "Quản lý barcode";
        return View(vm);
    }
}