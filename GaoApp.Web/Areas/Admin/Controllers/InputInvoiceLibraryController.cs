using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Infrastructure.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Route("admin/input-invoices")]
[Authorize(Policy = PermissionCodes.Inventory.StockDocument.View)]
public sealed class InputInvoiceLibraryController(InputInvoiceCatalogService service, IAuthorizationService authorization,
    ILogger<InputInvoiceLibraryController> logger) : BaseAdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewBag.CanReview = (await authorization.AuthorizeAsync(User, PermissionCodes.Inventory.StockDocument.Approve)).Succeeded;
        ViewBag.CanImport = (await authorization.AuthorizeAsync(User, PermissionCodes.Inventory.StockDocument.Update)).Succeeded;
        return View();
    }
    [HttpGet("list")]
    public Task<IActionResult> List([FromQuery] InvoiceCatalogFilter filter, CancellationToken ct) =>
        Run(async () => Json(await service.ListAsync(CurrentStoreId, filter, ct)));
    [HttpGet("{id:int}")]
    public Task<IActionResult> Detail(int id, CancellationToken ct) => Run(async () => Json(await service.DetailAsync(CurrentStoreId, id, ct)));
    [HttpGet("{id:int}/preview")]
    public Task<IActionResult> Preview(int id, CancellationToken ct) => Run(async () =>
        PartialView("~/Areas/Admin/Views/StockDocumentManagement/_InputInvoiceXmlPreview.cshtml", await service.PreviewAsync(CurrentStoreId, id, ct)));
    [HttpGet("{id:int}/pdf")]
    public Task<IActionResult> Pdf(int id, CancellationToken ct) => Run(async () =>
    {
        var pdf = await service.PdfAsync(CurrentStoreId, id, ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(pdf.Content, "application/pdf");
    });
    [HttpGet("{id:int}/xml")]
    public Task<IActionResult> Xml(int id, CancellationToken ct) => Run(async () =>
        File(await service.XmlAsync(CurrentStoreId, id, ct), "application/xml", $"hoa-don-{id}.xml"));
    [HttpPost("{id:int}/review"), ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Approve)]
    public Task<IActionResult> Review(int id, [FromBody] InvoiceCatalogReviewRequest request, CancellationToken ct) => Run(async () =>
    {
        await service.ReviewAsync(CurrentStoreId, id, CurrentUserId, request, ct);
        return Json(new { message = "Đã lưu xác nhận và lịch sử kiểm tra." });
    });
    [HttpPost("sync"), ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Update)]
    public Task<IActionResult> Sync(int? year, int? month, CancellationToken ct) => Run(async () =>
        Json(await service.SyncAsync(CurrentStoreId, year, month, ct)));
    [HttpPost("upload"), ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Update)]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public Task<IActionResult> Upload(IFormFile? xml, IFormFile? pdf, CancellationToken ct) => Run(async () =>
    {
        if (xml == null || xml.Length is <= 0 or > 10 * 1024 * 1024 || !Path.GetExtension(xml.FileName).Equals(".xml", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Chọn file XML tối đa 10 MB." });
        if (pdf != null && (pdf.Length > 50 * 1024 * 1024 || !Path.GetExtension(pdf.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { message = "Chọn PDF tối đa 50 MB." });
        using var xmlBuffer = new MemoryStream(); await xml.CopyToAsync(xmlBuffer, ct);
        using var pdfBuffer = new MemoryStream(); if (pdf != null) await pdf.CopyToAsync(pdfBuffer, ct);
        return Json(await service.UploadAsync(CurrentStoreId, xmlBuffer.ToArray(), pdf == null ? null : pdfBuffer.ToArray(), ct));
    });
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "no-store";
        if (!ModelState.IsValid) return BadRequest(new { message = "Dữ liệu gửi lên không hợp lệ." });
        try { return await action(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (BusinessRuleException ex) { return BadRequest(new { message = ex.Message }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Dữ liệu đã thay đổi. Mở lại hóa đơn trước khi lưu." }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException)
        {
            logger.LogWarning(ex, "Input invoice library operation failed for store {StoreId}", CurrentStoreId);
            return StatusCode(409, new { message = "Chưa hoàn tất thao tác. Kiểm tra nguồn thư viện hoặc tải lại nếu đang có người cập nhật. Các mục đã nhập vẫn được giữ lại." });
        }
    }
}
