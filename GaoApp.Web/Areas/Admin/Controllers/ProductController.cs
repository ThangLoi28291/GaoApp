using Microsoft.AspNetCore.Authorization;
using GaoApp.Web.Security;
using GaoApp.Application.Common.Security;
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
    private const string AllLifecycle = "all";
    private const string PosAllowedLifecycle = "pos-allowed";
    private const string NotForPosLifecycle = "not-for-pos";
    private const string InactiveLifecycle = "inactive";
    private const string ProductSetupFreshIdKey = "ProductSetupFreshId";

    private readonly IAuthorizationService _authorization;
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
        IProductVariantService variantService,
        IAuthorizationService authorization)
    {
        _authorization = authorization;
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

    private static string NormalizeLifecycle(string? lifecycle)
    {
        if (string.IsNullOrWhiteSpace(lifecycle))
            return PosAllowedLifecycle;

        return lifecycle.Trim().ToLowerInvariant() switch
        {
            AllLifecycle => AllLifecycle,
            PosAllowedLifecycle => PosAllowedLifecycle,
            NotForPosLifecycle => NotForPosLifecycle,
            InactiveLifecycle => InactiveLifecycle,
            _ => PosAllowedLifecycle
        };
    }

    private static (bool? IsActive, bool? IsSellable) ResolveLifecycle(
        string? lifecycle)
        => lifecycle switch
        {
            PosAllowedLifecycle => (true, true),
            NotForPosLifecycle => (true, false),
            InactiveLifecycle => (false, null),
            _ => (null, null)
        };

    private async Task<ProductIndexVM> BuildProductIndexAsync(
        int storeId,
        string? search,
        int? categoryId,
        string? lifecycle,
        int page,
        int pageSize,
        bool includeCategoryOptions,
        ProductListFilters filters,
        CancellationToken ct)
    {
        if (categoryId <= 0)
            categoryId = null;

        lifecycle = NormalizeLifecycle(lifecycle);
        var (isActive, isSellable) = ResolveLifecycle(lifecycle);

        var paged = await _productService.SearchCatalogAsync(
            storeId,
            search,
            categoryId,
            isActive,
            isSellable,
            page,
            pageSize,
            filters,
            ct);

        var summary = await _productService.GetSummaryAsync(storeId, ct);

        IReadOnlyList<ProductCategoryFilterOptionVM> categoryOptions =
            Array.Empty<ProductCategoryFilterOptionVM>();

        if (includeCategoryOptions)
        {
            var categories = await _categoryService.GetPagedAsync(
                search: null,
                page: 1,
                pageSize: DropdownPageSize,
                ct);

            categoryOptions = categories.Items
                .OrderBy(x => x.Name)
                .Select(x => new ProductCategoryFilterOptionVM
                {
                    Id = x.Id,
                    Name = x.Name
                })
                .ToList();
        }

        return new ProductIndexVM
        {
            SearchString = search,
            CategoryId = categoryId,
            Filters = filters,
            Lifecycle = lifecycle,
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalProductCount = summary.TotalItems,
            PosAllowedProductCount = summary.PosAllowedItems,
            NotForPosProductCount = summary.NotForPosItems,
            InactiveProductCount = summary.InactiveItems,
            SupplierOption = filters.SupplierId.HasValue ? (await _productService.FilterOptionsAsync(storeId, "supplier", null, filters.SupplierId, ct)).SingleOrDefault() : null,
            BrandOption = filters.BrandId.HasValue ? (await _productService.FilterOptionsAsync(storeId, "brand", null, filters.BrandId, ct)).SingleOrDefault() : null,
            UnitOption = filters.BaseUnitId.HasValue ? (await _productService.FilterOptionsAsync(storeId, "unit", null, filters.BaseUnitId, ct)).SingleOrDefault() : null,
            CategoryOptions = categoryOptions,
            Paged = paged
        };
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
    [Authorize(Policy = PermissionCodes.Catalog.Product.View)]

    public async Task<IActionResult> Index(
        string? search = "",
        int? categoryId = null,
        string? lifecycle = null,
        int page = 1,
        int pageSize = 20,
        int? supplierId = null,
        int? brandId = null,
        int? baseUnitId = null,
        string? dataIssue = null,
        CancellationToken ct = default)
    {
        if (supplierId.HasValue && !(await _authorization.AuthorizeAsync(User, PermissionCodes.Catalog.Supplier.View)).Succeeded) return Forbid();
        if (search?.Length > 200) return BadRequest(new { message = "Từ khóa tìm kiếm tối đa 200 ký tự." });
        if (dataIssue is not (null or "" or "no-brand" or "no-image" or "no-barcode")) return BadRequest(new { message = "Bộ lọc thông tin không hợp lệ." });
        var filters = new ProductListFilters(supplierId > 0 ? supplierId : null, brandId > 0 ? brandId : null, baseUnitId > 0 ? baseUnitId : null, dataIssue);
        var storeId = RequireStoreId();
        Normalize(ref page, ref pageSize);

        var vm = await BuildProductIndexAsync(
            storeId,
            search,
            categoryId,
            lifecycle,
            page,
            pageSize,
            includeCategoryOptions: true,
            filters: filters,
            ct: ct);

        return View(vm);
    }
    [Authorize(Policy = PermissionCodes.Catalog.Product.View)]

    public async Task<IActionResult> Search(
        string? search = "",
        int? categoryId = null,
        string? lifecycle = null,
        int page = 1,
        int pageSize = 20,
        int? supplierId = null,
        int? brandId = null,
        int? baseUnitId = null,
        string? dataIssue = null,
        CancellationToken ct = default)
    {
        if (supplierId.HasValue && !(await _authorization.AuthorizeAsync(User, PermissionCodes.Catalog.Supplier.View)).Succeeded) return Forbid();
        if (search?.Length > 200) return BadRequest(new { message = "Từ khóa tìm kiếm tối đa 200 ký tự." });
        if (dataIssue is not (null or "" or "no-brand" or "no-image" or "no-barcode")) return BadRequest(new { message = "Bộ lọc thông tin không hợp lệ." });
        var filters = new ProductListFilters(supplierId > 0 ? supplierId : null, brandId > 0 ? brandId : null, baseUnitId > 0 ? baseUnitId : null, dataIssue);
        var storeId = RequireStoreId();
        Normalize(ref page, ref pageSize);

        var vm = await BuildProductIndexAsync(
            storeId,
            search,
            categoryId,
            lifecycle,
            page,
            pageSize,
            includeCategoryOptions: false,
            filters: filters,
            ct: ct);

        return PartialView("_ProductTable", vm);
    }

    [HttpGet, Authorize(Policy = PermissionCodes.Catalog.Product.View)]
    public async Task<IActionResult> FilterOptions(string kind, string? term, int? selectedId, CancellationToken ct)
    {
        if (kind == "supplier" && !(await _authorization.AuthorizeAsync(User, PermissionCodes.Catalog.Supplier.View)).Succeeded) return Forbid();
        return Json(await _productService.FilterOptionsAsync(RequireStoreId(), kind, term, selectedId, ct));
    }

    [HttpGet]
    [RequireAnyPermission(PermissionCodes.Catalog.Product.Create, PermissionCodes.Catalog.Product.Update)]
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
    [Authorize(Policy = PermissionCodes.Catalog.Product.Create)]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var storeId = RequireStoreId();
        await PopulateDropdownsAsync(storeId, ct);
        return View(new ProductCreateDto());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Product.Create)]
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

        TempData["ToastSuccess"] = "Đã tạo sản phẩm. Tiếp tục thiết lập biến thể.";
        TempData[ProductSetupFreshIdKey] = result.Value;

        var editUrl = Url.Action(nameof(Edit), new { id = result.Value })
            ?? $"/Admin/Product/Edit/{result.Value}";

        return Redirect($"{editUrl}#variants");
    }

    // =========================================================
    // EDIT
    // =========================================================

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Catalog.Product.Update)]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var storeId = RequireStoreId();
        await PopulateDropdownsAsync(storeId, ct);

        var model = await _productService.GetForEditAsync(storeId, id, ct);
        if (model == null) return NotFound();

        var isInitialProductSetup = false;
        if (TempData.TryGetValue(ProductSetupFreshIdKey, out var freshProductId) &&
            int.TryParse(freshProductId?.ToString(), out var parsedFreshProductId) &&
            parsedFreshProductId == id)
        {
            isInitialProductSetup = true;
        }

        ViewBag.IsInitialProductSetup = isInitialProductSetup;

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Product.Update)]
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
    [Authorize(Policy = PermissionCodes.Catalog.Product.Update)]
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
    [Authorize(Policy = PermissionCodes.Catalog.Product.Delete)]
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
    [Authorize(Policy = PermissionCodes.Catalog.Product.View)]
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
    [Authorize(Policy = PermissionCodes.Catalog.Product.View)]
    public async Task<IActionResult> AttributesData(CancellationToken ct)
    {
        var storeId = RequireStoreId();
        var data = await _variantService.GetAttributesAsync(storeId, ct);
        return Json(new { ok = true, data });
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Catalog.Product.View)]
    public async Task<IActionResult> VariantsData(int productId, CancellationToken ct)
    {
        var storeId = RequireStoreId();
        var data = await _variantService.GetVariantsAsync(storeId, productId, ct);
        return Json(new { ok = true, data });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.ProductVariant.Create)]
    [Authorize(Policy = PermissionCodes.Catalog.ProductVariant.Update)]
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
    [Authorize(Policy = PermissionCodes.Catalog.Product.View)]
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
    [Authorize(Policy = PermissionCodes.Catalog.ProductVariant.Update)]
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
    [Authorize(Policy = PermissionCodes.Catalog.ProductVariant.Delete)]
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
    [Authorize(Policy = PermissionCodes.Catalog.ProductVariant.Update)]
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
