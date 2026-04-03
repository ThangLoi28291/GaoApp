using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Services.Brands;
using GaoApp.Application.Interfaces.Services.Categories;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Interfaces.Services.Suppliers;
using GaoApp.Application.Interfaces.Services.Taxes;
using GaoApp.Application.Interfaces.Services.Units;
using GaoApp.Web.Areas.Admin.ViewModels.Products;
using GaoApp.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
public class ProductController : BaseAdminController
{
    private const int DropdownPageSize = 500;

    private readonly ITenantContext _tenant;
    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;
    private readonly ISupplierService _supplierService;
    private readonly IBrandService _brandService;
    private readonly ITaxService _taxService;
    private readonly IUnitService _unitService;
    private readonly IProductVariantService _variantService;

    public ProductController(
        ITenantContext tenant,
        IProductService productService,
        ICategoryService categoryService,
        ISupplierService supplierService,
        IBrandService brandService,
        ITaxService taxService,
        IUnitService unitService,
        IProductVariantService variantService)
    {
        _tenant = tenant;
        _productService = productService;
        _categoryService = categoryService;
        _supplierService = supplierService;
        _brandService = brandService;
        _taxService = taxService;
        _unitService = unitService;
        _variantService = variantService;
    }

    private int RequireStoreId()
    {
        if (_tenant.StoreId == null)
            throw new InvalidOperationException("Thiếu StoreId (TenantContext).");

        return _tenant.StoreId.Value;
    }

    private static void Normalize(ref int page, ref int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;
    }

    private async Task PopulateDropdownsAsync(int storeId, CancellationToken ct)
    {
        var cats = await _categoryService.GetPagedAsync(null, 1, DropdownPageSize, ct);
        var sups = await _supplierService.GetPagedAsync(storeId, null, 1, DropdownPageSize, ct);
        var brands = await _brandService.GetPagedAsync(storeId, null, 1, DropdownPageSize, ct);
        var taxes = await _taxService.GetPagedAsync(storeId, null, 1, DropdownPageSize, ct);
        var units = await _unitService.GetPagedAsync(storeId, null, 1, DropdownPageSize, ct);

        ViewBag.Categories = cats.Items.Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToList();
        ViewBag.Suppliers = sups.Items.Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToList();
        ViewBag.Brands = brands.Items.Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToList();
        ViewBag.Taxes = taxes.Items.Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToList();
        ViewBag.Units = units.Items.Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToList();
    }

    // =========================================================
    // PRODUCT LIST
    // =========================================================

    public async Task<IActionResult> Index(string? search, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var storeId = RequireStoreId();
        Normalize(ref page, ref pageSize);

        var paged = await _productService.GetPagedAsync(storeId, search, page, pageSize, ct);

        var vm = new ProductIndexVM
        {
            SearchString = search,
            Page = page,
            PageSize = pageSize,
            Paged = paged
        };

        return View(vm);
    }

    public async Task<IActionResult> Search(string? search, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var storeId = RequireStoreId();
        Normalize(ref page, ref pageSize);

        var paged = await _productService.GetPagedAsync(storeId, search, page, pageSize, ct);

        var vm = new ProductIndexVM
        {
            SearchString = search,
            Page = page,
            PageSize = pageSize,
            Paged = paged
        };

        return PartialView("_ProductTable", vm);
    }

    [HttpGet]
    public async Task<IActionResult> GetUniqueAlias(string input, int? excludeId, CancellationToken ct)
    {
        var storeId = RequireStoreId();
        var alias = await _productService.GetUniqueAliasAsync(storeId, input ?? "", excludeId, ct);
        return Json(new { alias });
    }

    // =========================================================
    // CREATE
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var storeId = RequireStoreId();
        await PopulateDropdownsAsync(storeId, ct);
        return View(new ProductCreateDto());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductCreateDto dto, string? TempImageTokensJson, CancellationToken ct)
    {
        var storeId = RequireStoreId();

        dto.TempImageTokens = string.IsNullOrWhiteSpace(TempImageTokensJson)
            ? new List<string>()
            : (System.Text.Json.JsonSerializer.Deserialize<List<string>>(TempImageTokensJson) ?? new List<string>());

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(storeId, ct);
            return View(dto);
        }

        var result = await _productService.CreateAsync(storeId, dto, userId: null, ct);
        if (!result.IsSuccess)
        {
            ModelState.AddResultErrors(result);
            await PopulateDropdownsAsync(storeId, ct);
            return View(dto);
        }

        TempData["ToastSuccess"] = "Đã tạo sản phẩm.";
        return RedirectToAction(nameof(Index));
    }

    // =========================================================
    // EDIT
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var storeId = RequireStoreId();
        await PopulateDropdownsAsync(storeId, ct);

        var model = await _productService.GetForEditAsync(storeId, id, ct);
        if (model == null) return NotFound();

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        UpdateProductRequest dto,
        string? returnUrl,
        CancellationToken ct)
    {
        var storeId = RequireStoreId();

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(storeId, ct);

            var editVm = await _productService.GetForEditAsync(storeId, dto.Id, ct);
            if (editVm == null) return NotFound();

            ViewBag.ReturnUrl = returnUrl;
            return View(editVm);
        }

        var result = await _productService.UpdateAsync(storeId, dto, userId: null, ct);
        if (!result.IsSuccess)
        {
            ModelState.AddResultErrors(result);

            await PopulateDropdownsAsync(storeId, ct);
            var editVm = await _productService.GetForEditAsync(storeId, dto.Id, ct);
            if (editVm == null) return NotFound();

            ViewBag.ReturnUrl = returnUrl;
            return View(editVm);
        }

        TempData["ToastSuccess"] = "Đã cập nhật sản phẩm.";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction(nameof(Index));
    }

    // =========================================================
    // PRODUCT STATUS / DELETE
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(int id, CancellationToken ct)
    {
        var storeId = RequireStoreId();

        var result = await _productService.ToggleStatusAsync(storeId, id, userId: null, ct);
        if (!result.IsSuccess)
        {
            return Json(new
            {
                ok = false,
                message = result.Error?.Message ?? "Không thể đổi trạng thái sản phẩm."
            });
        }

        return Json(new { ok = true, message = "Đã đổi trạng thái." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SoftDelete(int id, CancellationToken ct)
    {
        var storeId = RequireStoreId();

        var result = await _productService.SoftDeleteAsync(storeId, id, userId: null, ct);
        if (!result.IsSuccess)
        {
            return Json(new
            {
                ok = false,
                message = result.Error?.Message ?? "Xóa sản phẩm thất bại."
            });
        }

        return Json(new { ok = true, message = "Đã xóa sản phẩm." });
    }

    // =========================================================
    // DETAIL
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var storeId = RequireStoreId();
        var model = await _productService.GetDetailGalleryAsync(storeId, id, ct);
        if (model == null) return NotFound();

        return View(model);
    }

    // =========================================================
    // VARIANT (Attribute-based)
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> AttributesData(CancellationToken ct)
    {
        var storeId = RequireStoreId();
        var data = await _variantService.GetAttributesAsync(storeId, ct);
        return Json(new { ok = true, data });
    }

    [HttpGet]
    public async Task<IActionResult> VariantsData(int productId, CancellationToken ct)
    {
        var storeId = RequireStoreId();
        var data = await _variantService.GetVariantsAsync(storeId, productId, ct);
        return Json(new { ok = true, data });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveVariants([FromBody] SaveProductVariantsRequest req, CancellationToken ct)
    {
        if (req == null || req.ProductId <= 0)
            return Json(new { ok = false, message = "Dữ liệu không hợp lệ." });

        var storeId = RequireStoreId();

        var result = await _variantService.SaveVariantsAsync(
            storeId,
            req.ProductId,
            req.Variants ?? new List<ProductVariantRowDto>(),
            userId: null,
            ct);

        return Json(new
        {
            ok = result.IsSuccess,
            message = result.IsSuccess ? "Đã lưu biến thể." : result.Error?.Message
        });
    }

    [HttpGet]
    public async Task<IActionResult> ImagesData(int productId, CancellationToken ct)
    {
        var storeId = RequireStoreId();
        var images = await _productService.GetImagesForVariantAsync(storeId, productId, ct);
        return Json(new { ok = true, data = images });
    }

    public sealed class IdRequest
    {
        public int Id { get; set; }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleVariantStatus([FromBody] IdRequest req, CancellationToken ct)
    {
        var storeId = RequireStoreId();

        if (req == null || req.Id <= 0)
            return Json(new { ok = false, message = "Id không hợp lệ." });

        var result = await _variantService.ToggleStatusAsync(storeId, req.Id, userId: null, ct);

        return Json(new
        {
            ok = result.IsSuccess,
            message = result.IsSuccess ? "Đã đổi trạng thái biến thể." : result.Error?.Message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteVariant([FromBody] IdRequest req, CancellationToken ct)
    {
        var storeId = RequireStoreId();

        if (req == null || req.Id <= 0)
            return Json(new { ok = false, message = "Id không hợp lệ." });

        var result = await _variantService.SoftDeleteVariantAsync(storeId, req.Id, userId: null, ct);

        return Json(new
        {
            ok = result.IsSuccess,
            message = result.IsSuccess ? "Đã xóa biến thể (ẩn khỏi danh sách)." : result.Error?.Message
        });
    }

    public sealed class SetVariantImageRequest
    {
        public int VariantId { get; set; }
        public int? PrimaryProductImageId { get; set; }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetVariantImage([FromBody] SetVariantImageRequest req, CancellationToken ct)
    {
        if (req is null || req.VariantId <= 0)
            return Json(new { ok = false, message = "Dữ liệu không hợp lệ." });

        var storeId = RequireStoreId();

        var result = await _variantService.SetVariantImageAsync(
            storeId,
            req.VariantId,
            req.PrimaryProductImageId,
            userId: null,
            ct);

        return Json(new
        {
            ok = result.IsSuccess,
            message = result.IsSuccess ? "Đã lưu ảnh biến thể." : result.Error?.Message
        });
    }
}