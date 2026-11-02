using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Web.Areas.Admin.ViewModels.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public partial class InvoiceController
{
    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] InvoiceListQueryDto query,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(InvoiceReadLimits.InvalidQueryMessage);
        query.NormalizeAndValidate();

        var result = await _invoiceService.GetInvoicesAsync(query, ct);

        if (!result.IsSuccess)
        {
            FlashError(InvoiceErrorReadSanitizer.UserMessage(result.Error));
            return View(new InvoiceIndexViewModel
            {
                Query = query,
                Items = new List<InvoiceListItemDto>(),
                TotalItems = 0,
                CurrentPage = query.Page,
                TotalPages = 0
            });
        }

        var data = result.Value;

        var totalPages = data.PageSize <= 0
            ? 0
            : (int)Math.Ceiling(data.TotalItems / (double)data.PageSize);

        var vm = new InvoiceIndexViewModel
        {
            Query = query,
            Items = data.Items,
            TotalItems = data.TotalItems,
            CurrentPage = data.Page,
            TotalPages = totalPages
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Detail(
        int id,
        CancellationToken ct)
    {
        var result = await _invoiceService.GetInvoiceDetailAsync(id, ct);
        await LoadCorrectionHistoryViewBagAsync(id, ct);

        if (!result.IsSuccess)
        {
            FlashError(InvoiceErrorReadSanitizer.UserMessage(result.Error));
            return RedirectToInvoiceIndexPage();
        }

        var vm = new InvoiceDetailViewModel
        {
            Invoice = result.Value
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> InvoiceSummaryPartial(
        int id,
        CancellationToken ct)
    {
        var result = await _invoiceService.GetInvoiceDetailAsync(id, ct);

        if (!result.IsSuccess)
            return BadRequest(InvoiceErrorReadSanitizer.UserMessage(result.Error));

        return PartialView("_InvoiceSummary", result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> ManualLinesPartial(
        int id,
        CancellationToken ct)
    {
        var result = await _invoiceService.GetInvoiceDetailAsync(id, ct);

        if (!result.IsSuccess)
            return BadRequest(InvoiceErrorReadSanitizer.UserMessage(result.Error));

        return PartialView("_ManualLinesTable", result.Value);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SearchProductVariants(
        string keyword,
        int invoiceHeadId,
        [FromServices] GaoApp.Application.Interfaces.Repositories.Invoices.IInvoiceProductPickerRepository picker,
        CancellationToken ct)
    {
        var result = await picker.SearchProductsAsync(invoiceHeadId, keyword, 20, ct);
        if (result.IsFailure) return result.Error.Code == "NotFound" ? NotFound(new { message = result.Error.Message }) : BadRequest(new { message = result.Error.Message });
        return Json(result.Value);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ProductStock(int invoiceHeadId, int productVariantId,
        [FromServices] GaoApp.Application.Interfaces.Repositories.Invoices.IInvoiceProductPickerRepository picker, CancellationToken ct)
    {
        var result = await picker.GetProductStockAsync(invoiceHeadId, productVariantId, ct);
        if (result.IsFailure) return result.Error.Code == "NotFound" ? NotFound(new { message = result.Error.Message }) : BadRequest(new { message = result.Error.Message });
        return Json(result.Value);
    }
}
