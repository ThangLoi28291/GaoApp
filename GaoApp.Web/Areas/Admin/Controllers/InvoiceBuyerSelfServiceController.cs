using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GaoApp.Web.Controllers;

[AllowAnonymous]
[Route("invoice-info")]
public sealed class InvoiceBuyerSelfServiceController
    : Controller
{
    private readonly IInvoiceBuyerSelfServiceService _service;

    public InvoiceBuyerSelfServiceController(
        IInvoiceBuyerSelfServiceService service)
    {
        _service = service;
    }

    [HttpGet(
        "{token}",
        Name = "invoice-buyer-self-service")]
    [EnableRateLimiting(
        "invoice-buyer-self-service")]
    public async Task<IActionResult> Index(
        string token,
        CancellationToken ct)
    {
        var result =
            await _service.GetAsync(
                token,
                ct);

        if (!result.IsSuccess)
        {
            return NotFound(
                "Không tìm thấy yêu cầu thông tin hóa đơn.");
        }

        if (TempData[
                "InvoiceBuyerSelfServiceSuccess"]
            is string success)
        {
            ViewData["SuccessMessage"] =
                success;
        }

        return View(result.Value);
    }

    [HttpPost("{token}")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(
        "invoice-buyer-self-service")]
    public async Task<IActionResult> Submit(
        string token,
        [FromForm]
        SubmitInvoiceBuyerSelfServiceRequest request,
        CancellationToken ct)
    {
        var result =
            await _service.SubmitAsync(
                token,
                request,
                ct);

        if (result.IsSuccess)
        {
            TempData[
                "InvoiceBuyerSelfServiceSuccess"] =
                "Đã tiếp nhận thông tin hóa đơn. " +
                "Cửa hàng sẽ kiểm tra và phát hành sau.";

            return RedirectToRoute(
                "invoice-buyer-self-service",
                new
                {
                    token
                });
        }

        if (result.Error?.Code ==
            "NotFound")
        {
            return NotFound(
                "Không tìm thấy yêu cầu thông tin hóa đơn.");
        }

        var current =
            await _service.GetAsync(
                token,
                ct);

        if (!current.IsSuccess)
        {
            return NotFound(
                "Không tìm thấy yêu cầu thông tin hóa đơn.");
        }

        ViewData["ErrorMessage"] =
            result.Error?.Message ??
            "Không thể lưu thông tin hóa đơn.";

        Response.StatusCode =
            result.Error?.Code == "Conflict"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;

        return View(
            "Index",
            current.Value);
    }
}