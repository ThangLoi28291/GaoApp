using GaoApp.Application.Common.Security;
using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public sealed class AutoInvoiceController : Controller
{
    private readonly IAutoInvoiceService _service;

    public AutoInvoiceController(IAutoInvoiceService service)
    {
        _service = service;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.System.AutoInvoice.Operate)]
    public async Task<IActionResult> Index(
        string? tab,
        string? month,
        AutoInvoiceScopeMode? scopeMode,
        DateTime? startDateLocal,
        DateTime? endDateLocal,
        CancellationToken ct,
        [FromServices] IAuthorizationService authorization,
        int page = 1,
        int pageSize = 50)
    {
        tab = tab?.ToLowerInvariant();
        if (tab is not ("today" or "waiting" or "single" or "groups" or "errors" or "history" or "old" or "settings" or "month"))
            tab = "today";
        var policy = tab == "settings" ? PermissionCodes.System.AutoInvoice.Settings : PermissionCodes.System.AutoInvoice.Operate;
        if (!(await authorization.AuthorizeAsync(User, policy)).Succeeded)
            return Forbid();
        if (string.Equals(tab, "month", StringComparison.OrdinalIgnoreCase) &&
            !startDateLocal.HasValue &&
            DateTime.TryParseExact(month, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedMonth))
        {
            startDateLocal = new DateTime(parsedMonth.Year, parsedMonth.Month, 1);
            scopeMode = AutoInvoiceScopeMode.Month;
        }

        var model = await _service.GetDashboardAsync(
            new AutoInvoiceDashboardQueryDto
            {
                Workspace = tab,
                ScopeMode = scopeMode ?? AutoInvoiceScopeMode.Today,
                StartDateLocal = startDateLocal,
                EndDateLocal = endDateLocal,
                Page = Math.Max(1, page),
                PageSize = Math.Clamp(pageSize, 1, 100)
            },
            ct);
        ViewBag.Tab = string.IsNullOrWhiteSpace(tab) ? "today" : tab;
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.System.AutoInvoice.Settings)]
    public async Task<IActionResult> SaveSettings(
        UpdateAutoInvoiceSettingsRequest request,
        CancellationToken ct)
    {
        // HTML number inputs submit invariant decimals, regardless of the
        // request culture. Do not let vi-VN binding interpret a decimal dot as
        // a thousands separator and silently multiply the configured amount.
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(ct);
            request.SeparateAmountThreshold = ReadAmount(nameof(request.SeparateAmountThreshold));
            request.GroupTargetAmount = ReadAmount(nameof(request.GroupTargetAmount));

            decimal ReadAmount(string name)
            {
                ModelState.Remove(name);
                var values = form[name];
                if (values.Count == 1 && decimal.TryParse(values[0],
                    System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                    System.Globalization.CultureInfo.InvariantCulture, out var amount))
                    return amount;
                ModelState.AddModelError(name, "Số tiền không hợp lệ.");
                return 0;
            }
        }
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Cấu hình chưa được lưu. Vui lòng kiểm tra số tiền, thời gian và phạm vi ngày.";
            return RedirectToAction(nameof(Index), new { tab = "settings" });
        }
        var result = await _service.UpdateSettingsAsync(request, ct);
        if (!result.IsSuccess)
            TempData["Error"] = result.Error?.Message;
        else
            TempData["Success"] = "Đã lưu cấu hình phát hành tự động.";
        return RedirectToAction(nameof(Index), new { tab = "settings" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.System.AutoInvoice.Settings)]
    public async Task<IActionResult> Toggle(CancellationToken ct)
    {
        var result = await _service.ToggleEnabledAsync(ct);
        var enabled = result.IsSuccess && result.Value;
        TempData[result.IsSuccess ? "Success" : "Error"] = result.IsSuccess
            ? enabled ? "Đã bật phát hành tự động." : "Đã tạm ngưng phát hành tự động."
            : result.Error?.Message;
        return RedirectToAction(nameof(Index), new { tab = "settings" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.System.AutoInvoice.Operate)]
    public async Task<IActionResult> RunOnce(CancellationToken ct)
    {
        var result = await _service.RunOnceAsync(force: true, ct);
        TempData[result.IsSuccess ? "Success" : "Error"] = result.IsSuccess
            ? "Worker đã nhận một chu kỳ xử lý."
            : result.Error?.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.System.AutoInvoice.Operate)]
    public async Task<IActionResult> RecheckIncident(
        int invoiceHeadId,
        [FromServices] ITenantContext tenant,
        [FromServices] IAutoInvoiceRepository repository,
        CancellationToken ct)
    {
        var invoice = ModelState.IsValid && invoiceHeadId > 0 && tenant.StoreId is > 0
            ? await repository.GetInvoiceHeadForClaimAsync(tenant.StoreId.Value, invoiceHeadId, ct)
            : null;
        if (invoice?.Order?.InvoiceIssuanceRoute != InvoiceIssuanceRoute.Automatic ||
            invoice.StoreId != tenant.StoreId || InvoiceIssuanceStatePolicy.IsIssuedLike(invoice) || invoice.IssuedAtUtc.HasValue)
        {
            TempData["Error"] = "Không tìm thấy hóa đơn tự động có thể kiểm tra lại trong cửa hàng hiện tại.";
            return RedirectToAction(nameof(Index), new { tab = "errors" });
        }
        var result = await _service.RecheckIncidentAsync(invoiceHeadId, ct);
        TempData[result.IsSuccess ? "Success" : "Error"] = result.IsSuccess
            ? "Đã kiểm tra lại điều kiện cục bộ của hóa đơn."
            : result.Error?.Message ?? "Không thể kiểm tra lại hóa đơn.";
        return RedirectToAction(nameof(Index), new { tab = "errors" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.System.Invoice.Route)]
    public async Task<IActionResult> ChangeToManual(
        int invoiceHeadId,
        [FromServices] ITenantContext tenant,
        [FromServices] IAutoInvoiceRepository repository,
        [FromServices] IInvoiceIssuanceRouteService routeService,
        CancellationToken ct)
    {
        var invoice = ModelState.IsValid && invoiceHeadId > 0 && tenant.StoreId is > 0
            ? await repository.GetInvoiceHeadForClaimAsync(tenant.StoreId.Value, invoiceHeadId, ct)
            : null;
        if (invoice?.Order?.InvoiceIssuanceRoute != InvoiceIssuanceRoute.Automatic ||
            invoice.StoreId != tenant.StoreId || invoice.OrderId is not > 0 ||
            invoice.IssuedAtUtc.HasValue || !InvoiceIssuanceStatePolicy.CanChangeRoute(invoice))
        {
            TempData["Error"] = "Hóa đơn không thuộc luồng tự động hoặc không còn ở trạng thái an toàn để chuyển.";
            return RedirectToAction(nameof(Index));
        }
        var active = await repository.GetActiveOperationsAsync(invoice.StoreId, ct);
        if (active.Any(operation => operation.InvoiceHeadId == invoiceHeadId ||
                operation.Sources.Any(source => !source.IsDeleted && source.InvoiceHeadId == invoiceHeadId)) ||
            await repository.HasActiveSourceAsync(invoice.StoreId, invoiceHeadId, ct) ||
            await repository.HasSuccessfulSourceAsync(invoice.StoreId, invoiceHeadId, ct))
        {
            TempData["Error"] = "Hóa đơn đang được xử lý hoặc đã được phát hành qua một operation.";
            return RedirectToAction(nameof(Index));
        }
        var result = await routeService.ChangeRouteAsync(invoice.OrderId.Value, InvoiceIssuanceRoute.Manual, ct);
        TempData[result.IsSuccess ? "Success" : "Error"] = result.IsSuccess
            ? "Đã chuyển đơn hàng sang luồng phát hành thủ công."
            : result.Error?.Message ?? "Không thể chuyển sang thủ công.";
        return RedirectToAction(nameof(Index));
    }
}
