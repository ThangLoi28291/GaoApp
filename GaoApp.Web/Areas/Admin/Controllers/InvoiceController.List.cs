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
        if (query.Page <= 0)
            query.Page = 1;

        if (query.PageSize <= 0)
            query.PageSize = 20;

        var result = await _invoiceService.GetInvoicesAsync(query, ct);

        if (!result.IsSuccess)
        {
            FlashError(result.Error?.Message);
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
            FlashError(result.Error?.Message);
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
            return BadRequest(result.Error?.Message);

        return PartialView("_InvoiceSummary", result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> ManualLinesPartial(
        int id,
        CancellationToken ct)
    {
        var result = await _invoiceService.GetInvoiceDetailAsync(id, ct);

        if (!result.IsSuccess)
            return BadRequest(result.Error?.Message);

        return PartialView("_ManualLinesTable", result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> SearchProductVariants(
        string keyword,
        CancellationToken ct)
    {
        var items = await _invoiceService.SearchProductVariantsAsync(
            keyword,
            20,
            ct);

        return AjaxProductVariantSearch(items);
    }
}