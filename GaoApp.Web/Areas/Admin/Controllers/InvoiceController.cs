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
    private readonly IViettelInvoicePayloadBuilder _viettelPayloadBuilder;
    private readonly IViettelInvoicePreviewService _viettelPreviewService;
    private readonly IViettelInvoiceIssueService _viettelIssueService;
    private readonly IViettelOfficialFileService _viettelOfficialFileService;
    private readonly IViettelInvoiceSyncService _viettelInvoiceSyncService;
    private readonly IViettelInvoiceEmailService _viettelInvoiceEmailService;
    public InvoiceController(IInvoiceService invoiceService, 
        IViettelInvoicePayloadBuilder viettelPayloadBuilder, 
        IViettelInvoicePreviewService viettelPreviewService, 
        IViettelInvoiceIssueService viettelIssueService, 
        IViettelOfficialFileService viettelOfficialFileService,
        IViettelInvoiceSyncService viettelInvoiceSyncService,
        IViettelInvoiceEmailService viettelInvoiceEmailService)
    {
        _invoiceService = invoiceService;
        _viettelPayloadBuilder = viettelPayloadBuilder;
        _viettelPreviewService = viettelPreviewService;
        _viettelIssueService = viettelIssueService;
        _viettelOfficialFileService = viettelOfficialFileService;
        _viettelInvoiceSyncService = viettelInvoiceSyncService;
        _viettelInvoiceEmailService = viettelInvoiceEmailService;
    }

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
            TempData["Error"] = result.Error?.Message;

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
    public async Task<IActionResult> ViettelPayload(int id, CancellationToken ct)
    {
        var result = await _viettelPayloadBuilder.BuildAsync(id, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message;
            return RedirectToAction(nameof(Detail), new { id });
        }

        return View("ViettelPayload", result.Value);
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ViettelIssue(int id, CancellationToken ct)
    {
        var result = await _viettelIssueService.IssueAsync(id, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message ?? "Phát hành hóa đơn Viettel thất bại.";
            return RedirectToAction(nameof(ViettelPayload), new { id });
        }

        var data = result.Value;

        TempData["Success"] = string.IsNullOrWhiteSpace(data.InvoiceNo)
            ? "Đã gửi phát hành Viettel thành công, nhưng chưa nhận được số hóa đơn. Cần tra cứu lại bằng transactionUuid."
            : $"Đã phát hành hóa đơn Viettel thành công. Số hóa đơn: {data.InvoiceNo}.";

        return RedirectToAction(nameof(ViettelPayload), new { id });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncViettelByUuid(int id, CancellationToken ct)
    {
        var result = await _viettelInvoiceSyncService.SyncByTransactionUuidAsync(id, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] =
                result.Error?.Message ?? "Tra cứu Viettel theo UUID thất bại.";

            return RedirectToAction(nameof(ViettelPayload), new { id });
        }

        var data = result.Value;

        if (data.IsFound)
        {
            TempData["Success"] = string.IsNullOrWhiteSpace(data.InvoiceNo)
                ? "Đã tìm thấy hóa đơn trên Viettel theo transactionUuid và đồng bộ trạng thái."
                : $"Đã đồng bộ hóa đơn Viettel thành công. Số hóa đơn: {data.InvoiceNo}.";

            return RedirectToAction(nameof(ViettelPayload), new { id });
        }

        TempData["Error"] =
            data.ErrorMessage ??
            "Không tìm thấy hóa đơn trên Viettel theo transactionUuid. Có thể phát hành lại bằng UUID cũ.";

        return RedirectToAction(nameof(ViettelPayload), new { id });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendViettelEmail(
    ViettelInvoiceSendEmailRequestDto request,
    CancellationToken ct)
    {
        var result = await _viettelInvoiceEmailService.SendEmailAsync(request, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message ?? "Gửi email hóa đơn Viettel thất bại.";

            return RedirectToAction(
                nameof(ViettelPayload),
                new { id = request.InvoiceHeadId });
        }

        TempData["Success"] =
            $"Đã gửi email hóa đơn cho: {result.Value.BuyerEmail}";

        return RedirectToAction(
            nameof(ViettelPayload),
            new { id = request.InvoiceHeadId });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadOfficialPdf(int id, CancellationToken ct)
    {
        var result = await _viettelOfficialFileService.DownloadAndSaveAsync(
            id,
            ViettelOfficialFileType.Pdf,
            ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message ?? "Tải PDF chính thức thất bại.";
            return RedirectToAction(nameof(ViettelPayload), new { id });
        }

        TempData["Success"] = $"Đã tải và lưu PDF chính thức: {result.Value.FileName}";

        return RedirectToAction(nameof(ViettelPayload), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadOfficialZipXml(int id, CancellationToken ct)
    {
        var result = await _viettelOfficialFileService.DownloadAndSaveAsync(
            id,
            ViettelOfficialFileType.ZipXml,
            ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message ?? "Tải ZIP/XML chính thức thất bại.";
            return RedirectToAction(nameof(ViettelPayload), new { id });
        }

        TempData["Success"] = $"Đã tải và lưu ZIP/XML chính thức: {result.Value.FileName}";

        return RedirectToAction(nameof(ViettelPayload), new { id });
    }


    [HttpGet]
    public async Task<IActionResult> ViewOfficialPdf(int id, CancellationToken ct)
    {
        var result = await _viettelOfficialFileService.ReadSavedAsync(
            id,
            ViettelOfficialFileType.Pdf,
            ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message ?? "Chưa có PDF chính thức.";
            return RedirectToAction(nameof(ViettelPayload), new { id });
        }

        Response.Headers["Content-Disposition"] =
            $"inline; filename=\"{result.Value.FileName}\"";

        return File(result.Value.FileBytes, result.Value.ContentType);
    }

    [HttpGet]
    public async Task<IActionResult> DownloadSavedZipXml(int id, CancellationToken ct)
    {
        var result = await _viettelOfficialFileService.ReadSavedAsync(
            id,
            ViettelOfficialFileType.ZipXml,
            ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message ?? "Chưa có ZIP/XML chính thức.";
            return RedirectToAction(nameof(ViettelPayload), new { id });
        }

        return File(
            result.Value.FileBytes,
            result.Value.ContentType,
            result.Value.FileName);
    }
    [HttpGet]
    public async Task<IActionResult> ViettelDraftPreview(int id, CancellationToken ct)
    {
        var result = await _viettelPreviewService.PreviewDraftPdfAsync(id, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message ?? "Không xem trước được hóa đơn nháp Viettel.";
            return RedirectToAction(nameof(ViettelPayload), new { id });
        }

        var file = result.Value;

        Response.Headers["Content-Disposition"] =
            $"inline; filename=\"{file.FileName}\"";

        return File(
            file.FileBytes,
            file.ContentType);
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

         // GHI CHÚ:
         // Truyền ProductVariantId để service biết dòng manual này đang chọn sản phẩm nào.
         // Nếu không truyền, service không thể tự lấy tên sản phẩm, đơn vị, giá bán.
         ProductVariantId = vm.ProductVariantId,

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