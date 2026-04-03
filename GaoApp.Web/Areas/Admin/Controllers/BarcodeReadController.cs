using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Services.Products;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers.Api;

[Area("Admin")]
[ApiController]
[Route("admin/api/barcode-read")]
public sealed class BarcodeReadController : ControllerBase
{
    private readonly IProductUnitBarcodeReadService _barcodeReadService;
    private readonly IBarcodeHistoryService _barcodeHistoryService;

    public BarcodeReadController(
        IProductUnitBarcodeReadService barcodeReadService,
        IBarcodeHistoryService barcodeHistoryService)
    {
        _barcodeReadService = barcodeReadService;
        _barcodeHistoryService = barcodeHistoryService;
    }

    [HttpGet("conversion/{conversionId:int}/barcodes")]
    public async Task<IActionResult> GetBarcodes(int conversionId, CancellationToken ct)
    {
        var items = await _barcodeReadService.GetByConversionIdAsync(conversionId, ct);
        return Ok(items);
    }

    [HttpGet("conversion/{conversionId:int}/history")]
    public async Task<IActionResult> GetHistory(int conversionId, CancellationToken ct)
    {
        var result = await _barcodeHistoryService.GetPagedAsync(new BarcodeHistoryQueryRequest
        {
            ProductUnitConversionId = conversionId,
            Page = 1,
            PageSize = 20
        }, ct);

        return Ok(result);
    }
}