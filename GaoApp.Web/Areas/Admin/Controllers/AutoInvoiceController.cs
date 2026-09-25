using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionCodes.System.Integration.Manage)]
public sealed class AutoInvoiceController : Controller
{
    private readonly IAutoInvoiceService _service;

    public AutoInvoiceController(IAutoInvoiceService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? tab,
        string? month,
        AutoInvoiceScopeMode? scopeMode,
        DateTime? startDateLocal,
        DateTime? endDateLocal,
        CancellationToken ct)
    {
        if (string.Equals(tab, "month", StringComparison.OrdinalIgnoreCase) &&
            !startDateLocal.HasValue &&
            DateTime.TryParseExact(month, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedMonth))
        {
            startDateLocal = new DateTime(parsedMonth.Year, parsedMonth.Month, 1);
            scopeMode = AutoInvoiceScopeMode.Month;
        }
        else if (string.Equals(tab, "month", StringComparison.OrdinalIgnoreCase) && !startDateLocal.HasValue)
        {
            var today = DateTime.Today;
            startDateLocal = new DateTime(today.Year, today.Month, 1);
            scopeMode = AutoInvoiceScopeMode.Month;
        }

        var model = await _service.GetDashboardAsync(
            new AutoInvoiceDashboardQueryDto
            {
                ScopeMode = scopeMode ?? AutoInvoiceScopeMode.Today,
                StartDateLocal = startDateLocal,
                EndDateLocal = endDateLocal
            },
            ct);
        ViewBag.Tab = string.IsNullOrWhiteSpace(tab) ? "today" : tab;
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(
        UpdateAutoInvoiceSettingsRequest request,
        CancellationToken ct)
    {
        var result = await _service.UpdateSettingsAsync(request, ct);
        if (!result.IsSuccess)
            TempData["Error"] = result.Error?.Message;
        else
            TempData["Success"] = "Đã lưu cấu hình phát hành tự động.";
        return RedirectToAction(nameof(Index), new { tab = "settings" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(CancellationToken ct)
    {
        var result = await _service.ToggleEnabledAsync(ct);
        var enabled = result.IsSuccess && result.Value;
        TempData[result.IsSuccess ? "Success" : "Error"] = result.IsSuccess
            ? enabled ? "Đã bật phát hành tự động." : "Đã tạm ngưng phát hành tự động."
            : result.Error?.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunOnce(CancellationToken ct)
    {
        var result = await _service.RunOnceAsync(force: true, ct);
        TempData[result.IsSuccess ? "Success" : "Error"] = result.IsSuccess
            ? "Worker đã nhận một chu kỳ xử lý."
            : result.Error?.Message;
        return RedirectToAction(nameof(Index));
    }
}
