using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Route("admin/storefront/products")]
public sealed class StorefrontProductsController(IStorefrontProductService service) : BaseAdminController
{
    [HttpGet(""), Authorize(Policy = StorefrontPermissions.ViewProducts)]
    public async Task<IActionResult> Index([FromQuery] StorefrontProductQuery query, CancellationToken ct) => View(await service.ListAsync(query, ct));

    [HttpGet("{id:int}"), Authorize(Policy = StorefrontPermissions.ViewProducts)]
    public async Task<IActionResult> Edit(int id, CancellationToken ct) => View(await service.GetAsync(id, ct));

    [HttpPost("{id:int}"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.ManageProducts)]
    [RequestFormLimits(ValueCountLimit = 65000), RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IActionResult> Save(int id, [Bind(Prefix = "Product")] StorefrontProductRequest request, CancellationToken ct)
    {
        // Empty slots in the media editor are UI controls, not asset selections.
        request.Media = request.Media?.Where(x => x.MediaAssetId != 0).ToList() ?? [];
        if (ModelState.IsValid)
        {
            try { await service.SaveAsync(id, request, ct); ToastSuccess("Đã lưu hàng website."); return RedirectToAction(nameof(Edit), new { id }); }
            catch (ValidationAppException ex) { ModelState.AddModelError("", ex.Message); Response.StatusCode = 400; }
            catch (ConflictAppException ex) { ModelState.AddModelError("", ex.Message); Response.StatusCode = 409; }
        }
        else Response.StatusCode = 400;
        return View("Edit", (await service.GetAsync(id, ct)) with { Product = request });
    }

    [HttpPost("publication"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.ManageProducts)]
    public async Task<IActionResult> Publication(StorefrontPublicationRequest request, CancellationToken ct)
        => View("PublicationResults", new[] { await service.SetPublicationAsync(request, ct) });

    [HttpPost("publication/bulk"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.ManageProducts)]
    public async Task<IActionResult> Bulk(List<int> selectedProductIds, List<StorefrontPublicationRequest> items, bool publish, CancellationToken ct)
    {
        if (selectedProductIds.Count is < 1 or > 100 || selectedProductIds.Distinct().Count() != selectedProductIds.Count ||
            items.Count > 100 || selectedProductIds.Any(id => items.Count(x => x.ProductId == id) != 1))
            return BadRequest("Chọn từ 1 đến 100 sản phẩm trên trang, không trùng mã.");
        var request = new StorefrontBulkPublicationRequest { Items = items.Where(x => selectedProductIds.Contains(x.ProductId)).ToList() };
        foreach (var item in request.Items) item.IsPublished = publish;
        return View("PublicationResults", await service.SetPublicationBulkAsync(request, ct));
    }

    [HttpGet("media"), Authorize(Policy = StorefrontPermissions.ViewProducts)]
    public async Task<IActionResult> Media(string? search, int page = 1, CancellationToken ct = default)
    {
        ViewBag.Search = search; ViewBag.Page = page;
        return View(await service.SearchMediaAsync(search, page, ct));
    }
}
