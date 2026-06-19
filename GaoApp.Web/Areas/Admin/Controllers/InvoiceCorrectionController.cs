using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Enums;
using GaoApp.Web.Areas.Admin.ViewModels.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN")]
public class InvoiceCorrectionController : Controller
{
    private readonly IInvoiceCorrectionService _correctionService;

    public InvoiceCorrectionController(
        IInvoiceCorrectionService correctionService)
    {
        _correctionService = correctionService;
    }

    [HttpGet]
    public async Task<IActionResult> Create(
        int originalInvoiceHeadId,
        InvoiceCorrectionType type,
        CancellationToken ct)
    {
        var infoResult = await _correctionService.GetCreateInfoAsync(
            originalInvoiceHeadId,
            type,
            ct);

        if (!infoResult.IsSuccess)
        {
            TempData["Error"] = infoResult.Error?.Message ?? "Không thể lập hóa đơn xử lý sai sót.";

            return RedirectToAction(
                "ViettelPayload",
                "Invoice",
                new { area = "Admin", id = originalInvoiceHeadId });
        }

        var info = infoResult.Value;

        return View(new InvoiceCorrectionCreateViewModel
        {
            Info = info,
            Request = new CreateInvoiceCorrectionRequestDto
            {
                OriginalInvoiceHeadId = originalInvoiceHeadId,
                Type = type,
                AgreementDocumentNo = info.DefaultAgreementDocumentNo,
                AgreementDate = info.DefaultAgreementDate
            }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CreateInvoiceCorrectionRequestDto request,
        CancellationToken ct)
    {
        var result = await _correctionService.CreateAsync(request, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message ?? "Không thể tạo hóa đơn xử lý sai sót.";

            var infoResult = await _correctionService.GetCreateInfoAsync(
                request.OriginalInvoiceHeadId,
                request.Type,
                ct);

            if (!infoResult.IsSuccess)
            {
                return RedirectToAction(
                    "ViettelPayload",
                    "Invoice",
                    new { area = "Admin", id = request.OriginalInvoiceHeadId });
            }

            return View(new InvoiceCorrectionCreateViewModel
            {
                Info = infoResult.Value,
                Request = request
            });
        }

        TempData["Success"] =
            $"Đã tạo {result.Value.TypeName}. Vui lòng kiểm tra lại dữ liệu trước khi phát hành Viettel.";

        return RedirectToAction(
            "Detail",
            "Invoice",
            new { area = "Admin", id = result.Value.NewInvoiceHeadId });
    }
}