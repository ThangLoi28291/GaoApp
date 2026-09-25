using Microsoft.AspNetCore.Authorization;
using GaoApp.Web.Security;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Brands;
using GaoApp.Application.Interfaces.Services.Brands;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Areas.Admin.ViewModels.Brands;
using GaoApp.Web.Common.Responses;
using GaoApp.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public class BrandController : BaseAdminController
{
    private readonly IBrandService _service;
    private readonly ITenantContext _tenant;

    public BrandController(IBrandService service, ITenantContext tenant)
    {
        _service = service;
        _tenant = tenant;
    }

    private int StoreId() => _tenant.StoreId ?? throw new InvalidOperationException("StoreId not found");

    private static void NormalizePagination(ref int page, ref int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Catalog.Brand.View)]
    public async Task<IActionResult> Index(
        string? search = "",
        bool? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        NormalizePagination(ref page, ref pageSize);

        var storeId = StoreId();
        var paged = await _service.GetPagedAsync(storeId, search, status, page, pageSize, ct);
        var summary = await _service.GetSummaryAsync(storeId, ct);

        return View(CreateIndexViewModel(search, status, page, pageSize, paged, summary));
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Catalog.Brand.View)]
    public async Task<IActionResult> Search(
        string? search = "",
        bool? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        NormalizePagination(ref page, ref pageSize);

        var storeId = StoreId();
        var paged = await _service.GetPagedAsync(storeId, search, status, page, pageSize, ct);
        var summary = await _service.GetSummaryAsync(storeId, ct);

        return PartialView(
            "_BrandTable",
            CreateIndexViewModel(search, status, page, pageSize, paged, summary));
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Catalog.Brand.Create)]
    public IActionResult Create()
    {
        return View("Edit", new BrandEditViewModel
        {
            Status = true
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Brand.Create)]
    public async Task<IActionResult> Create(
        BrandEditViewModel vm,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return View("Edit", vm);

        var result = await _service.CreateAsync(
            StoreId(),
            new CreateBrandRequest
            {
                Code = vm.Code,
                Name = vm.Name,
                Description = vm.Description,
                Status = vm.Status
            },
            userId: null,
            ct: ct);

        if (!result.IsSuccess)
        {
            ModelState.AddResultErrors(result);
            return View("Edit", vm);
        }

        TempData["ToastSuccess"] = "Đã tạo thương hiệu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Catalog.Brand.Update)]
    public async Task<IActionResult> Edit(int id, CancellationToken ct = default)
    {
        var result = await _service.GetForEditAsync(StoreId(), id, ct);

        if (!result.IsSuccess)
        {
            TempData["ToastError"] = result.Error.Message;
            return RedirectToAction(nameof(Index));
        }

        var dto = result.Value;

        return View(new BrandEditViewModel
        {
            Id = dto.Id,
            Code = dto.Code,
            Name = dto.Name,
            Description = dto.Description,
            Status = dto.Status,
            RowVersion = dto.RowVersion
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Brand.Update)]
    public async Task<IActionResult> Edit(
        BrandEditViewModel vm,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return View(vm);

        var result = await _service.UpdateAsync(
            StoreId(),
            new UpdateBrandRequest
            {
                Id = vm.Id,
                Code = vm.Code!,
                Name = vm.Name,
                Description = vm.Description,
                Status = vm.Status,
                RowVersion = vm.RowVersion
            },
            userId: null,
            ct: ct);

        if (!result.IsSuccess)
        {
            ModelState.AddResultErrors(result);
            return View(vm);
        }

        TempData["ToastSuccess"] = "Đã cập nhật thương hiệu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Brand.Update)]
    public async Task<IActionResult> ToggleStatus(int id, CancellationToken ct = default)
    {
        var result = await _service.ToggleStatusAsync(StoreId(), id, userId: null, ct: ct);

        return Json(AjaxResponse.FromResult(result, "Đã cập nhật trạng thái."));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Brand.Delete)]
    public async Task<IActionResult> DeleteAjax(int id, CancellationToken ct = default)
    {
        var result = await _service.SoftDeleteAsync(StoreId(), id, userId: null, ct: ct);

        return Json(AjaxResponse.FromResult(result, "Đã xóa thương hiệu."));
    }

    private static BrandIndexVM CreateIndexViewModel(
        string? search,
        bool? status,
        int page,
        int pageSize,
        PagedResult<BrandListItemDto> paged,
        (int TotalItems, int ActiveItems, int InactiveItems) summary)
        => new()
        {
            SearchString = search,
            Status = status,
            Page = page,
            PageSize = pageSize,
            TotalBrandCount = summary.TotalItems,
            ActiveBrandCount = summary.ActiveItems,
            InactiveBrandCount = summary.InactiveItems,
            Paged = paged
        };
}
