using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Units;
using GaoApp.Application.Interfaces.Services.Units;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Areas.Admin.ViewModels.Units;
using GaoApp.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public class UnitController : BaseAdminController
{
    private readonly IUnitService _service;
    private readonly ITenantContext _tenant;

    public UnitController(IUnitService service, ITenantContext tenant)
    {
        _service = service;
        _tenant = tenant;
    }

    private int CurrentStoreId()
    {
        if (_tenant.StoreId is null)
            throw new InvalidOperationException("Không xác định StoreId (Tenant).");
        return _tenant.StoreId.Value;
    }

    private static void NormalizePagination(ref int page, ref int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search = "", int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        NormalizePagination(ref page, ref pageSize);

        var storeId = CurrentStoreId();
        var paged = await _service.GetPagedAsync(storeId, search, page, pageSize, ct);

        return View(new UnitIndexVM
        {
            SearchString = search,
            Page = page,
            PageSize = pageSize,
            Paged = paged
        });
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? search = "", int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        NormalizePagination(ref page, ref pageSize);

        var storeId = CurrentStoreId();
        var paged = await _service.GetPagedAsync(storeId, search, page, pageSize, ct);

        return PartialView("_UnitTable", new UnitIndexVM
        {
            SearchString = search,
            Page = page,
            PageSize = pageSize,
            Paged = paged
        });
    }

    [HttpGet]
    public IActionResult Create()
        => View("Edit", new UnitEditViewModel { Status = true });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UnitEditViewModel vm, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return View("Edit", vm);

        var storeId = CurrentStoreId();

        var result = await _service.CreateAsync(
            storeId,
            new CreateUnitRequest
            {
                Code = vm.Code,
                Name = vm.Name,
                Status = vm.Status,
                IsBase = vm.IsBase,
                SortOrder = vm.SortOrder
            },
            userId: null,
            ct);

        if (!result.IsSuccess)
        {
            ModelState.AddResultErrors(result);
            return View("Edit", vm);
        }

        TempData["ToastSuccess"] = "Đã tạo đơn vị.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct = default)
    {
        var storeId = CurrentStoreId();
        var result = await _service.GetForEditAsync(storeId, id, ct);

        if (!result.IsSuccess)
        {
            TempData["ToastError"] = result.Error.Message;
            return RedirectToAction(nameof(Index));
        }

        var dto = result.Value;

        return View(new UnitEditViewModel
        {
            Id = dto.Id,
            Code = dto.Code,
            Name = dto.Name,
            Status = dto.Status,
            IsBase = dto.IsBase,
            SortOrder = dto.SortOrder,
            RowVersion = dto.RowVersion
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UnitEditViewModel vm, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return View(vm);

        var storeId = CurrentStoreId();

        var result = await _service.UpdateAsync(
            storeId,
            new UpdateUnitRequest
            {
                Id = vm.Id,
                Code = vm.Code ?? string.Empty,
                Name = vm.Name,
                Status = vm.Status,
                IsBase = vm.IsBase,
                SortOrder = vm.SortOrder,
                RowVersion = vm.RowVersion
            },
            userId: null,
            ct);

        if (!result.IsSuccess)
        {
            ModelState.AddResultErrors(result);
            return View(vm);
        }

        TempData["ToastSuccess"] = "Đã cập nhật đơn vị.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(int id, CancellationToken ct = default)
    {
        var storeId = CurrentStoreId();
        var result = await _service.ToggleStatusAsync(storeId, id, userId: null, ct);

        return Json(new
        {
            success = result.IsSuccess,
            message = result.IsSuccess ? "Đã cập nhật trạng thái." : result.Error.Message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAjax(int id, CancellationToken ct = default)
    {
        var storeId = CurrentStoreId();
        var result = await _service.SoftDeleteAsync(storeId, id, userId: null, ct);

        return Json(new
        {
            success = result.IsSuccess,
            message = result.IsSuccess ? "Đã xóa đơn vị." : result.Error.Message
        });
    }
}