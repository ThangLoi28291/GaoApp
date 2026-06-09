using System.Security.Claims;
using GaoApp.Application.DTOs.Products.BarcodeVerification;
using GaoApp.Application.Interfaces.Services.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/api/stock-documents/{stockDocumentId:int}/barcode-verification")]
[Authorize]
[ApiController]
public class BarcodeVerificationController : ControllerBase
{
    private readonly IProductBarcodeVerificationService _service;

    public BarcodeVerificationController(IProductBarcodeVerificationService service)
    {
        _service = service;
    }

    [HttpGet("missing")]
    public async Task<IActionResult> Missing(int stockDocumentId, CancellationToken ct)
    {
        var storeId = GetCurrentStoreId();

        var items = await _service.GetMissingUnitsForStockDocumentAsync(
            stockDocumentId,
            storeId,
            ct);

        return Ok(new
        {
            count = items.Count,
            items
        });
    }

    [HttpPost("requests")]
    public async Task<IActionResult> Submit(
        int stockDocumentId,
        [FromBody] SubmitBarcodeVerificationRequest request,
        CancellationToken ct)
    {
        var storeId = GetCurrentStoreId();
        var userId = GetCurrentUserId();

        try
        {
            var count = await _service.SubmitRequestsAsync(
                stockDocumentId,
                storeId,
                userId,
                request,
                ct);

            return Ok(new
            {
                message = count > 0
                    ? $"Đã gửi {count} yêu cầu chuẩn hóa barcode."
                    : "Không có barcode nào cần gửi.",
                count
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    private int GetCurrentStoreId()
    {
        var value =
            User.FindFirst("StoreId")?.Value ??
            User.FindFirst("store_id")?.Value;

        if (int.TryParse(value, out var storeId) && storeId > 0)
            return storeId;

        throw new InvalidOperationException("Không xác định được StoreId.");
    }

    private int GetCurrentUserId()
    {
        var value =
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            User.FindFirst("UserId")?.Value ??
            User.FindFirst("user_id")?.Value;

        if (int.TryParse(value, out var userId) && userId > 0)
            return userId;

        throw new InvalidOperationException("Không xác định được UserId.");
    }
}