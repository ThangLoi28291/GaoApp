using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Web.ViewModels.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class InvoiceController : Controller
{
    private readonly IInvoiceService _invoiceService;

    public InvoiceController(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] InvoiceListQueryDto query,
        CancellationToken ct)
    {
        var result = await _invoiceService.GetInvoicesAsync(query, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message;

            return View(new InvoiceIndexViewModel
            {
                Query = query
            });
        }

        var data = result.Value;

        var vm = new InvoiceIndexViewModel
        {
            Query = query,
            Items = data.Items,
            TotalItems = data.TotalItems,
            CurrentPage = data.Page,
            TotalPages = (int)Math.Ceiling(
                data.TotalItems / (double)data.PageSize)
        };

        return View(vm);
    }
    [HttpGet]
    public async Task<IActionResult> Detail(
    int id,
    CancellationToken ct)
    {
        var result = await _invoiceService.GetInvoiceDetailAsync(id, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message;

            return RedirectToAction(nameof(Index));
        }

        var vm = new InvoiceDetailViewModel
        {
            Invoice = result.Value
        };

        return View(vm);
    }
    [HttpGet]
    public IActionResult CreateManualDetail(int invoiceHeadId)
    {
        var vm = new CreateManualInvoiceDetailViewModel
        {
            InvoiceHeadId = invoiceHeadId,
            Quantity = 1,
            VatRate = 0
        };

        return PartialView("_CreateManualDetailModal", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateManualDetail(
        CreateManualInvoiceDetailViewModel vm,
        CancellationToken ct)
    {
        var result = await _invoiceService.AddManualDetailAsync(
            new CreateManualInvoiceDetailRequest
            {
                InvoiceHeadId = vm.InvoiceHeadId,
                ItemName = vm.ItemName,
                UnitName = vm.UnitName,
                Quantity = vm.Quantity,
                UnitPrice = vm.UnitPrice,
                VatRate = vm.VatRate,
                Note = vm.Note
            },
            ct);

        if (!result.IsSuccess)
        {
            return BadRequest(new
            {
                success = false,
                message = result.Error?.Message ?? "Không thêm được dòng manual."
            });
        }

        return Json(new
        {
            success = true,
            message = "Đã thêm dòng manual.",
            invoiceHeadId = result.Value.Id
        });
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
        var items = await _invoiceService.SearchProductVariantsAsync(keyword, 20, ct);

        return Json(items.Select(x => new
        {
            id = x.ProductVariantId,
            text = x.DisplayName,
            sku = x.Sku,
            barcode = x.Barcode,
            unitName = x.UnitName,
            unitPrice = x.UnitPrice
        }));
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteManualDetail(
    int invoiceDetailId,
    CancellationToken ct)
    {
        var result = await _invoiceService.DeleteManualDetailAsync(invoiceDetailId, ct);

        if (!result.IsSuccess)
        {
            return BadRequest(new
            {
                success = false,
                message = result.Error?.Message ?? "Không xóa được dòng manual."
            });
        }

        return Json(new
        {
            success = true,
            message = "Đã xóa dòng manual.",
            invoiceHeadId = result.Value.Id
        });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateManualDetail(
    UpdateManualInvoiceDetailRequest request,
    CancellationToken ct)
    {
        var result = await _invoiceService.UpdateManualDetailAsync(request, ct);

        if (!result.IsSuccess)
        {
            return BadRequest(new
            {
                success = false,
                message = result.Error?.Message ?? "Không cập nhật được dòng manual."
            });
        }

        return Json(new
        {
            success = true,
            message = "Đã cập nhật dòng manual.",
            invoiceHeadId = result.Value.Id
        });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Lock(
    int invoiceHeadId,
    string? reason,
    CancellationToken ct)
    {
        var result = await _invoiceService.LockInvoiceAsync(
            new LockInvoiceRequest
            {
                InvoiceHeadId = invoiceHeadId,
                Reason = reason,
                UserId = null
            },
            ct);

        if (!result.IsSuccess)
        {
            return BadRequest(new
            {
                success = false,
                message = result.Error?.Message ?? "Không khóa được hóa đơn."
            });
        }

        return Json(new
        {
            success = true,
            message = "Đã khóa hóa đơn.",
            invoiceHeadId = result.Value.Id,
            isLocked = result.Value.IsLocked
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(
        int invoiceHeadId,
        string? reason,
        CancellationToken ct)
    {
        var result = await _invoiceService.UnlockInvoiceAsync(
            new UnlockInvoiceRequest
            {
                InvoiceHeadId = invoiceHeadId,
                Reason = reason,
                UserId = null
            },
            ct);

        if (!result.IsSuccess)
        {
            return BadRequest(new
            {
                success = false,
                message = result.Error?.Message ?? "Không mở khóa được hóa đơn."
            });
        }

        return Json(new
        {
            success = true,
            message = "Đã mở khóa hóa đơn.",
            invoiceHeadId = result.Value.Id,
            isLocked = result.Value.IsLocked
        });
    }
}