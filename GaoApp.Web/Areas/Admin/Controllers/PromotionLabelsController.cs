using GaoApp.Application.Common.Security;
using GaoApp.Web.Services.Printing;
using GaoApp.Infrastructure.Printing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Route("admin/promotion-labels")]
[Authorize(Policy = PermissionCodes.Catalog.Promotion.Manage)]
[Authorize(Policy = PermissionCodes.System.ProductLabel.Manage)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PromotionLabelsController(PromotionLabelService labels, IWebHostEnvironment environment) : BaseAdminController
{
    [HttpGet("")]
    public IActionResult Index() { ViewBag.StoreId = CurrentStoreId; return View(); }

    [HttpPost("preview-a4"), RequestSizeLimit(2 * 1024 * 1024)]
    public IActionResult PreviewA4([FromBody] PromotionA4Preview request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (request.Payload?.Design?.IsA4 != true) return BadRequest(new { message = "Chọn mẫu A4 để xem cả tờ." });
        if (!OperatingSystem.IsWindows()) return StatusCode(503, new { message = "Kết nối bộ in client để xem mẫu A4 trên máy này." });
        // Rendering only; the server never submits this sheet to its own printer.
        return File(PromotionA4Layout.Preview(request.Payload, request.Page), "image/png");
    }

    [HttpGet("client-download")]
    public IActionResult ClientDownload()
    {
        var path = Path.Combine(environment.ContentRootPath, "ClientPackages", "GaoApp.LabelPrintClient-win-x64.zip");
        return System.IO.File.Exists(path) ? PhysicalFile(path, "application/zip", "GaoApp.LabelPrintClient-win-x64.zip")
            : NotFound(new { message = "Chưa có gói client trong bản triển khai. Chạy scripts/printing/Build-LabelClientPackage.ps1 trước khi publish Web." });
    }

    [HttpGet("campaigns")]
    public async Task<IActionResult> Campaigns(CancellationToken ct) => Ok(await labels.Campaigns(ct));

    [HttpGet("products")]
    public async Task<IActionResult> Products(int promotionId, string? q, int page = 1, int pageSize = 24, CancellationToken ct = default)
        => Ok(await labels.Products(promotionId, q, page, pageSize, ct));

    [HttpPost("prepare")]
    public async Task<IActionResult> Prepare([FromBody] PreparePromotionLabels request, CancellationToken ct)
        => ModelState.IsValid ? Ok(await labels.Prepare(request, ct)) : ValidationProblem(ModelState);

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshPromotionLabels request, CancellationToken ct)
        => ModelState.IsValid ? Ok(await labels.Refresh(request, ct)) : ValidationProblem(ModelState);
}
