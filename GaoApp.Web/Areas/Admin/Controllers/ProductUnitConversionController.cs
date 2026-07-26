using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Services.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
[AutoValidateAntiforgeryToken]
public class ProductUnitConversionController : Controller
{
    private readonly IProductUnitConversionService _productUnitConversionService;
    private readonly IBarcodeLookupService _barcodeLookupService;

    public ProductUnitConversionController(
        IProductUnitConversionService productUnitConversionService,
        IBarcodeLookupService barcodeLookupService)
    {
        _productUnitConversionService = productUnitConversionService;
        _barcodeLookupService = barcodeLookupService;
    }

    [HttpGet]
    public async Task<IActionResult> GetByVariantId(int productVariantId, CancellationToken ct)
    {
        if (productVariantId <= 0)
        {
            return Json(new
            {
                ok = false,
                message = "productVariantId không hợp lệ."
            });
        }

        var data = await _productUnitConversionService.GetByVariantIdAsync(productVariantId, ct);

        return Json(new
        {
            ok = true,
            message = "Lấy dữ liệu thành công.",
            data
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveConversion([FromBody] ProductUnitConversionUpsertDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .SelectMany(x => x.Value!.Errors.Select(e => $"{x.Key}: {e.ErrorMessage}"))
                .ToList();

            return Json(new
            {
                ok = false,
                message = "Dữ liệu không hợp lệ.",
                errors
            });
        }

        try
        {
            var id = await _productUnitConversionService.SaveConversionAsync(dto, ct);

            return Json(new
            {
                ok = true,
                message = "Lưu quy đổi đơn vị thành công.",
                id
            });
        }
        catch (BusinessRuleException ex)
        {
            return Json(new
            {
                ok = false,
                message = ex.SafeMessage
            });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBarcode([FromBody] UpsertProductVariantUnitBarcodeRequest request, CancellationToken ct)
    {
        try
        {
            var id = await _productUnitConversionService.SaveBarcodeAsync(request, ct);

            return Json(new
            {
                ok = true,
                id,
                message = "Lưu barcode thành công."
            });
        }
        catch (BusinessRuleException ex)
        {
            return Json(new
            {
                ok = false,
                message = ex.SafeMessage
            });
        }
    }

    /// <summary>
    /// Lấy lịch sử barcode theo ProductUnitConversion để bind vào modal UI.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBarcodeHistoryByConversionId(int productUnitConversionId, int take = 20, CancellationToken ct = default)
    {
        if (productUnitConversionId <= 0)
        {
            return Json(new
            {
                ok = false,
                message = "productUnitConversionId không hợp lệ."
            });
        }

        var data = await _productUnitConversionService.GetBarcodeHistoryByConversionIdAsync(productUnitConversionId, take, ct);

        return Json(new
        {
            ok = true,
            message = "Lấy lịch sử barcode thành công.",
            data
        });
    }

    [HttpPost]
    public async Task<IActionResult> LookupBarcode([FromBody] BarcodeLookupRequest dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .SelectMany(x => x.Value!.Errors.Select(e => $"{x.Key}: {e.ErrorMessage}"))
                .ToList();

            return Json(new
            {
                ok = false,
                message = "Dữ liệu không hợp lệ.",
                errors
            });
        }

        var result = await _barcodeLookupService.FindAsync(dto.Barcode, ct);

        if (result == null)
        {
            return Json(new
            {
                ok = false,
                message = "Không tìm thấy barcode."
            });
        }

        return Json(new
        {
            ok = true,
            message = "Tra barcode thành công.",
            data = result
        });
    }
}
