using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Services.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
[AutoValidateAntiforgeryToken]
public class ProductUnitConversionController : Controller
{
    private readonly IProductUnitConversionService _productUnitConversionService;
    private readonly IBarcodeLookupService _barcodeLookupService;
    private readonly ILogger<ProductUnitConversionController> _logger;

    public ProductUnitConversionController(
        IProductUnitConversionService productUnitConversionService,
        IBarcodeLookupService barcodeLookupService,
        ILogger<ProductUnitConversionController> logger)
    {
        _productUnitConversionService = productUnitConversionService;
        _barcodeLookupService = barcodeLookupService;
        _logger = logger;
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

        try
        {
            var data = await _productUnitConversionService.GetByVariantIdAsync(productVariantId, ct);

            return Json(new
            {
                ok = true,
                message = "Lấy dữ liệu thành công.",
                data
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy ProductUnitConversion theo ProductVariantId = {ProductVariantId}", productVariantId);

            return Json(new
            {
                ok = false,
                message = ex.Message
            });
        }
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lưu ProductUnitConversion cho ProductVariantId = {ProductVariantId}", dto.ProductVariantId);

            return Json(new
            {
                ok = false,
                message = ex.Message
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
        catch (InvalidOperationException ex)
        {
            return Json(new
            {
                ok = false,
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return Json(new
            {
                ok = false,
                message = ex.InnerException?.Message ?? ex.Message
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

        try
        {
            var data = await _productUnitConversionService.GetBarcodeHistoryByConversionIdAsync(productUnitConversionId, take, ct);

            return Json(new
            {
                ok = true,
                message = "Lấy lịch sử barcode thành công.",
                data
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy lịch sử barcode cho ProductUnitConversionId = {ProductUnitConversionId}", productUnitConversionId);

            return Json(new
            {
                ok = false,
                message = ex.Message
            });
        }
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

        try
        {
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lookup barcode = {Barcode}", dto.Barcode);

            return Json(new
            {
                ok = false,
                message = ex.Message
            });
        }
    }
}