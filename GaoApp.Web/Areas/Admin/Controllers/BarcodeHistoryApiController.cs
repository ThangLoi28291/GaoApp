using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Services.Products;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers.Api;

[Area("Admin")]
[ApiController]
[Route("admin/api/barcode-history")]
public sealed class BarcodeHistoryApiController : ControllerBase
{
    private readonly IBarcodeHistoryService _barcodeHistoryService;

    public BarcodeHistoryApiController(IBarcodeHistoryService barcodeHistoryService)
    {
        _barcodeHistoryService = barcodeHistoryService;
    }

    [HttpGet("")]
    public async Task<IActionResult> GetPaged([FromQuery] BarcodeHistoryQueryRequest request, CancellationToken ct)
    {
        var result = await _barcodeHistoryService.GetPagedAsync(request, ct);

        var response = new BarcodeHistoryListResponseDto
        {
            Data = result
        };

        return Ok(response);
    }
}