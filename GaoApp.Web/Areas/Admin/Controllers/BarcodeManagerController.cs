using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Web.Areas.Admin.ViewModels.Products;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/barcode-manager")]
public sealed class BarcodeManagerController : Controller
{
    private readonly IProductUnitBarcodeReadService _barcodeReadService;

    public BarcodeManagerController(IProductUnitBarcodeReadService barcodeReadService)
    {
        _barcodeReadService = barcodeReadService;
    }

    [HttpGet("{conversionId:int}")]
    public async Task<IActionResult> Index(int conversionId, CancellationToken ct)
    {
        var header = await _barcodeReadService.GetManagerHeaderAsync(conversionId, ct);
        if (header == null)
            return NotFound();

        var vm = new BarcodeManagerVm
        {
            ProductUnitConversionId = header.ProductUnitConversionId,
            ProductName = header.ProductName,
            VariantSku = header.VariantSku,
            UnitName = header.UnitName
        };

        ViewData["Title"] = "Quản lý barcode";
        return View(vm);
    }
}