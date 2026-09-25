using GaoApp.Application.Common;
using GaoApp.Application.Common.Options;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Media;
using GaoApp.Infrastructure.Services.Media;
using GaoApp.Infrastructure.Storage;
using GaoApp.Web.Services.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), Route("admin/media-library")]
[Authorize(Policy = PermissionCodes.Catalog.Product.View)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MediaLibraryController(MediaLibraryService library, ITenantContext tenant,
    IAuthorizationService authorization, IOptions<MediaCleanupOptions> options, MediaCleanupStatus status) : Controller
{
    [HttpGet("data")]
    public async Task<IActionResult> Data([FromServices] IAntiforgery antiforgery,
        string? search, string? statusFilter, int page = 1, CancellationToken ct = default)
    {
        if (tenant.StoreId is not > 0) return BadRequest("Cần chọn cửa hàng.");
        var result = await library.ListAsync(search, statusFilter, page, ct);
        return Ok(new
        {
            items = result.Items.Select(ApiItem), result.Summary, result.Search, result.Status,
            result.Page, result.Pages, pageSize = 24, result.FilteredCount,
            canManage = (await authorization.AuthorizeAsync(User, PermissionCodes.Catalog.Product.Delete)).Succeeded,
            requestVerificationToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        if (tenant.StoreId is not > 0) return BadRequest("Cần chọn cửa hàng.");
        var item = await library.GetAsync(id, ct);
        return item == null ? NotFound() : Ok(ApiItem(item));
    }

    [HttpGet("policy")]
    public IActionResult Policy()
    {
        if (tenant.StoreId is not > 0) return BadRequest("Cần chọn cửa hàng.");
        return Ok(new { options = options.Value, lastRun = status.Get(tenant.StoreId.Value) });
    }

    // Expose authorized preview URLs, never storage paths or upload tokens.
    private object ApiItem(MediaLibraryItem item) => new
    {
        item.Id, item.Name, item.SizeBytes,
        createdAtUtc = DateTime.SpecifyKind(item.CreatedAtUtc, DateTimeKind.Utc),
        expireAtUtc = item.ExpireAtUtc.HasValue ? DateTime.SpecifyKind(item.ExpireAtUtc.Value, DateTimeKind.Utc) : (DateTime?)null,
        item.IsTemp, item.IsDeleted, item.Used, item.Status,
        previewUrl = item.Status == "deleted" ? null : Url.Action(nameof(Preview), new { id = item.Id }),
        products = item.Products.Select(product => new
        {
            product.Id, product.Name,
            url = Url.Action("Detail", "Product", new { area = "Admin", id = product.Id })
        })
    };

    [HttpGet("{id:int}/preview")]
    public async Task<IActionResult> Preview(int id, [FromServices] UploadPathResolver paths, CancellationToken ct)
    {
        if (tenant.StoreId is not > 0) return BadRequest("Cần chọn cửa hàng.");
        var path = await library.GetPreviewPathAsync(id, ct);
        if (path == null) return NotFound();
        var mime = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", ".gif" => "image/gif", _ => null
        };
        if (mime == null || !(path.StartsWith("uploads/products/", StringComparison.Ordinal) || path.StartsWith("uploads/_temp/", StringComparison.Ordinal))) return NotFound();
        try
        {
            var fullPath = paths.Resolve(path);
            if (!System.IO.File.Exists(fullPath) && paths.LegacyRoot != null)
                fullPath = UploadPathResolver.ResolveUnderRoot(paths.LegacyRoot, UploadPathResolver.Normalize(path)[8..]);
            if (!System.IO.File.Exists(fullPath)) return NotFound();
            Response.Headers.XContentTypeOptions = "nosniff";
            return PhysicalFile(fullPath, mime);
        }
        catch (InvalidOperationException) { return NotFound(); }
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, string? statusFilter, int page = 1, CancellationToken ct = default)
    {
        if (tenant.StoreId is not > 0) return BadRequest("Cần chọn cửa hàng.");
        ViewBag.CanManage = (await authorization.AuthorizeAsync(User, PermissionCodes.Catalog.Product.Delete)).Succeeded;
        ViewBag.CleanupOptions = options.Value;
        ViewBag.LastRun = status.Get(tenant.StoreId.Value);
        return View(await library.ListAsync(search, statusFilter, page, ct));
    }

    [HttpPost("cleanup"), Authorize(Policy = PermissionCodes.Catalog.Product.Delete)]
    public async Task<IActionResult> Cleanup(int afterId = 0, CancellationToken ct = default)
    {
        if (tenant.StoreId is not > 0) return BadRequest("Cần chọn cửa hàng.");
        var result = await library.SweepAsync(Math.Max(0, afterId), ct);
        status.Record(tenant.StoreId.Value, result);
        return Ok(new { result, hasMore = result.Scanned == options.Value.BatchSize });
    }

    [HttpPost("{id:int}/cleanup"), Authorize(Policy = PermissionCodes.Catalog.Product.Delete)]
    public async Task<IActionResult> CleanupOne(int id, CancellationToken ct)
    {
        if (tenant.StoreId is not > 0) return BadRequest("Cần chọn cửa hàng.");
        var result = await library.ProcessAsync(id, ct);
        if (result == MediaCleanupOutcome.Missing) return NotFound();
        return Ok(new { outcome = result.ToString() });
    }

    [HttpPost("{id:int}/cancel-temp"), Authorize(Policy = PermissionCodes.Catalog.Product.Delete)]
    public async Task<IActionResult> CancelTemp(int id, CancellationToken ct)
    {
        if (tenant.StoreId is not > 0) return BadRequest("Cần chọn cửa hàng.");
        return await library.CancelTempAsync(id, ct) ? Ok() : NotFound();
    }
}
