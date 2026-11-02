using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Storefront.Web.Models;
using GaoApp.Storefront.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GaoApp.Storefront.Web.Controllers;

public sealed class ShopController(IStorefrontCatalogService catalog, StorefrontCartReader cart, StorefrontMediaReader media) : Controller
{
    [HttpGet("/"), HttpGet("/shop")]
    public async Task<IActionResult> Home(CancellationToken ct) => View(new StorefrontHomePage(
        await catalog.SearchAsync(new() { PageSize = 8 }, ct), await catalog.GetGroupsAsync(ct)));

    [HttpGet("/shop/products")]
    public async Task<IActionResult> Products([FromQuery] StorefrontCatalogQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return PageError(400, "Bộ lọc không hợp lệ", "Hãy nhập lại từ khóa, thứ tự hoặc trang kết quả.");
        try { return View(new StorefrontProductsPage(query, await catalog.SearchAsync(query, ct), await catalog.GetGroupsAsync(ct))); }
        catch (ValidationAppException ex) { return PageError(400, "Bộ lọc không hợp lệ", ex.Message); }
    }

    [HttpGet("/shop/products/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var product = await StorefrontRequestTiming.TimeAsync(HttpContext, "catalog", () => catalog.GetAsync(id, ct));
        return product is null ? PageError(404, "Sản phẩm hiện không có trên website", "Sản phẩm có thể đã được ẩn hoặc đổi lựa chọn bán. Hãy quay lại danh mục để chọn món khác.") : View(product);
    }

    [HttpGet("/shop/cart")]
    public IActionResult Cart() => View();

    [HttpGet("/shop/orders")]
    public IActionResult Orders() => View();

    [HttpPost("/shop/cart/resolve"), ValidateAntiForgeryToken, EnableRateLimiting("cart-read")]
    [Consumes("application/json"), RequestSizeLimit(32768)]
    public async Task<IActionResult> Resolve([FromBody] StorefrontCartRequest? request, CancellationToken ct)
    {
        if (request is null || !ModelState.IsValid) return BadRequest(new { message = "Dữ liệu giỏ không hợp lệ. Hãy kiểm tra lại lựa chọn và số lượng." });
        try { return Json(await cart.ResolveAsync(request, ct)); }
        catch (ValidationAppException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("/shop/media/{id:int}")]
    public async Task<IActionResult> Media(int id, CancellationToken ct)
    {
        int? width = null;
        if (Request.Query.TryGetValue("width", out var values))
        {
            if (values.Count != 1 || !int.TryParse(values[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var requested) ||
                !StorefrontVisualLimits.IsImageWidth(requested)) return BadRequest();
            width = requested;
        }
        var asset = await media.OpenForRequestAsync(id, width, HttpContext, ct);
        if (asset is null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return File(asset.Content, asset.ContentType, enableRangeProcessing: false);
    }

    private IActionResult PageError(int status, string title, string message)
    {
        Response.StatusCode = status; ViewData["Title"] = title; ViewData["Message"] = message;
        return View("Error");
    }
}
