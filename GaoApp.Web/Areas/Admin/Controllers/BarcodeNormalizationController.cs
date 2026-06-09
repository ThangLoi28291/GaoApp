using System.Security.Claims;
using GaoApp.Application.DTOs.Products.BarcodeVerification;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/barcode-normalization")]
[Authorize]
public class BarcodeNormalizationController : Controller
{
    private readonly IProductBarcodeVerificationService _service;

    public BarcodeNormalizationController(
        IProductBarcodeVerificationService service)
    {
        _service = service;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet("list")]
    public async Task<IActionResult> List(
        [FromQuery] BarcodeVerificationRequestStatus? status,
        [FromQuery] string? keyword,
        CancellationToken ct)
    {
        var storeId = GetCurrentStoreId();

        var items = await _service.GetManagementListAsync(
            storeId,
            status,
            keyword,
            ct);

        return Ok(items);
    }

    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(
        int id,
        [FromBody] BarcodeVerificationResolveRequest request,
        CancellationToken ct)
    {
        try
        {
            await _service.ApproveAsync(
                GetCurrentStoreId(),
                id,
                GetCurrentUserId(),
                request.ManagerNote,
                ct);

            return Ok(new
            {
                message = "Đã duyệt barcode và thêm vào hệ thống."
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

    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(
        int id,
        [FromBody] BarcodeVerificationResolveRequest request,
        CancellationToken ct)
    {
        try
        {
            await _service.RejectAsync(
                GetCurrentStoreId(),
                id,
                GetCurrentUserId(),
                request.ManagerNote,
                ct);

            return Ok(new
            {
                message = "Đã từ chối yêu cầu barcode."
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

    [HttpPost("{id:int}/confirm-no-barcode")]
    public async Task<IActionResult> ConfirmNoBarcode(
        int id,
        [FromBody] BarcodeVerificationResolveRequest request,
        CancellationToken ct)
    {
        try
        {
            await _service.ConfirmNoBarcodeAsync(
                GetCurrentStoreId(),
                id,
                GetCurrentUserId(),
                request.ManagerNote,
                ct);

            return Ok(new
            {
                message = "Đã xác nhận đơn vị này không có barcode nhà sản xuất."
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