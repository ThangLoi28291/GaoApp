using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionCodes.System.Integration.View)]
public class InvoiceProviderSettingController : Controller
{
    private readonly IInvoiceProviderSettingService _service;

    public InvoiceProviderSettingController(IInvoiceProviderSettingService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var result = await _service.GetAllAsync(ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message;
            return View(new List<InvoiceProviderSettingListItemDto>());
        }

        return View(result.Value);
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.System.Integration.Manage)]
    public IActionResult Create()
    {
        var vm = new UpsertInvoiceProviderSettingRequest
        {
            ProviderCode = "VIETTEL",
            AuthMode = InvoiceProviderAuthMode.BasicAuth,
            IsProduction = false,
            BaseUrl = "https://api-vinvoice.viettel.vn/services/einvoiceapplication/api",
            InvoiceType = "1",
            CurrencyCode = "VND",
            ExchangeRate = 1m,
            PaymentMethodName = "TM",
            CusGetInvoiceRight = true,
            DefaultPaymentStatus = true,
            IsActive = true
        };

        return View("Form", vm);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.System.Integration.Manage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        UpsertInvoiceProviderSettingRequest vm,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View("Form", vm);

        var result = await _service.CreateAsync(vm, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message;
            return View("Form", vm);
        }

        TempData["Success"] = "Đã tạo cấu hình hóa đơn điện tử.";

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.System.Integration.Manage)]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var result = await _service.GetForEditAsync(id, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message;
            return RedirectToAction(nameof(Index));
        }

        return View("Form", result.Value);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.System.Integration.Manage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        UpsertInvoiceProviderSettingRequest vm,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View("Form", vm);

        var result = await _service.UpdateAsync(vm, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message;
            return View("Form", vm);
        }

        TempData["Success"] = "Đã cập nhật cấu hình hóa đơn điện tử.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.System.Integration.Manage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id, CancellationToken ct)
    {
        var result = await _service.ToggleActiveAsync(id, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message;
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = result.Value
            ? "Đã bật cấu hình."
            : "Đã tắt cấu hình.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.System.Integration.Manage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestLogin(int id, CancellationToken ct)
    {
        var result = await _service.TestLoginAsync(id, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message;
            return RedirectToAction(nameof(Index));
        }

        if (result.Value.IsSuccess)
        {
            TempData["Success"] =
                $"{result.Value.Message} Thời gian: {result.Value.DurationMs}ms.";
        }
        else
        {
            TempData["Error"] =
                $"{result.Value.Message} Thời gian: {result.Value.DurationMs}ms.";
        }
        return RedirectToAction(nameof(Index));
    }
}
