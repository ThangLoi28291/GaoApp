using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Services.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers.Api;

[Area("Admin")]
[Authorize]
[ApiController]
[Route("admin/api/barcodes")]
public sealed class BarcodeGovernanceController : ControllerBase
{
    private readonly IBarcodeLookupService _barcodeLookupService;
    private readonly IBarcodeGovernanceService _barcodeGovernanceService;
    private readonly IProductUnitBarcodeReadService _barcodeReadService;
    private readonly IBarcodeHistoryService _barcodeHistoryService;

    public BarcodeGovernanceController(
        IBarcodeLookupService barcodeLookupService,
        IBarcodeGovernanceService barcodeGovernanceService,
        IProductUnitBarcodeReadService barcodeReadService,
        IBarcodeHistoryService barcodeHistoryService)
    {
        _barcodeLookupService = barcodeLookupService;
        _barcodeGovernanceService = barcodeGovernanceService;
        _barcodeReadService = barcodeReadService;
        _barcodeHistoryService = barcodeHistoryService;
    }

    /// <summary>
    /// Lookup barcode dùng cho POS / nghiệp vụ quét mã.
    /// Có thể trả về:
    /// - mã hiện tại
    /// - mã lịch sử đã đổi
    /// </summary>
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] string barcode, CancellationToken ct)
    {
        var result = await _barcodeLookupService.LookupAsync(barcode, ct);
        return Ok(result);
    }

    /// <summary>
    /// Đổi barcode theo luồng governance chuẩn:
    /// - ngưng mã cũ
    /// - tạo mã mới
    /// - ghi lịch sử append-only
    /// </summary>
    [HttpPost("change")]
    public async Task<IActionResult> ChangeBarcode([FromBody] ChangeBarcodeRequest request, CancellationToken ct)
    {
        try
        {
            await _barcodeGovernanceService.ChangeBarcodeAsync(request, ct);

            return Ok(new
            {
                success = true,
                message = "Đổi barcode thành công."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Lấy danh sách barcode hiện có của 1 ProductUnitConversion.
    /// Trả về list thuần để admin UI render nhanh.
    /// </summary>
    [HttpGet("conversion/{conversionId:int}/list")]
    public async Task<IActionResult> GetBarcodesByConversion(int conversionId, CancellationToken ct)
    {
        var items = await _barcodeReadService.GetByConversionIdAsync(conversionId, ct);
        return Ok(items);
    }

    /// <summary>
    /// Lấy lịch sử barcode của 1 ProductUnitConversion.
    /// 
    /// Lưu ý:
    /// - endpoint này đang dùng service phân trang tổng quát
    /// - response là object phân trang, không phải list thuần
    /// - frontend phải đọc result.items thay vì đọc trực tiếp result
    /// </summary>
    [HttpGet("conversion/{conversionId:int}/history")]
    public async Task<IActionResult> GetHistoryByConversion(int conversionId, CancellationToken ct)
    {
        var result = await _barcodeHistoryService.GetPagedAsync(
            new BarcodeHistoryQueryRequest
            {
                ProductUnitConversionId = conversionId,
                Page = 1,
                PageSize = 20
            },
            ct);

        return Ok(result);
    }
}