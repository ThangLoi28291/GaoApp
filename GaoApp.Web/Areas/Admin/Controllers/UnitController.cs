using Microsoft.AspNetCore.Authorization;
using GaoApp.Web.Security;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Units;
using GaoApp.Application.Interfaces.Services.Units;
using GaoApp.Web.Areas.Admin.ViewModels.Units;
using GaoApp.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public class UnitController : BaseAdminController
{
    private readonly IUnitService _service;

    public UnitController(
        IUnitService service)
    {
        _service = service;
    }

    private static void NormalizePagination(
        ref int page,
        ref int pageSize)
    {
        if (page < 1)
            page = 1;

        if (pageSize < 1)
            pageSize = 20;

        if (pageSize > 200)
            pageSize = 200;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Catalog.Unit.View)]
    public async Task<IActionResult> Index(
        string? search = "",
        bool? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        NormalizePagination(
            ref page,
            ref pageSize);

        var storeId =
            CurrentStoreId;

        var paged =
            await _service.GetPagedAsync(
                storeId,
                search,
                status,
                page,
                pageSize,
                ct);

        var summary =
            await _service.GetSummaryAsync(
                storeId,
                ct);

        return View(
            CreateIndexViewModel(
                search,
                status,
                page,
                pageSize,
                paged,
                summary));
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Catalog.Unit.View)]
    public async Task<IActionResult> Search(
        string? search = "",
        bool? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        NormalizePagination(
            ref page,
            ref pageSize);

        var storeId =
            CurrentStoreId;

        var paged =
            await _service.GetPagedAsync(
                storeId,
                search,
                status,
                page,
                pageSize,
                ct);

        var summary =
            await _service.GetSummaryAsync(
                storeId,
                ct);

        return PartialView(
            "_UnitTable",
            CreateIndexViewModel(
                search,
                status,
                page,
                pageSize,
                paged,
                summary));
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Catalog.Unit.Create)]
    public IActionResult Create()
        => View(
            "Edit",
            new UnitEditViewModel
            {
                Status = true
            });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Unit.Create)]
    public async Task<IActionResult> Create(
        UnitEditViewModel vm,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return View(
                "Edit",
                vm);

        var storeId =
            CurrentStoreId;

        var result =
            await _service.CreateAsync(
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
            ModelState.AddResultErrors(
                result);

            return View(
                "Edit",
                vm);
        }

        TempData["ToastSuccess"] =
            "Đã tạo đơn vị.";

        return RedirectToAction(
            nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Catalog.Unit.Update)]
    public async Task<IActionResult> Edit(
        int id,
        CancellationToken ct = default)
    {
        var storeId =
            CurrentStoreId;

        var result =
            await _service.GetForEditAsync(
                storeId,
                id,
                ct);

        if (!result.IsSuccess)
        {
            TempData["ToastError"] =
                result.Error.Message;

            return RedirectToAction(
                nameof(Index));
        }

        var dto =
            result.Value;

        return View(
            new UnitEditViewModel
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
    [Authorize(Policy = PermissionCodes.Catalog.Unit.Update)]
    public async Task<IActionResult> Edit(
        UnitEditViewModel vm,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return View(vm);

        var storeId =
            CurrentStoreId;

        var result =
            await _service.UpdateAsync(
                storeId,
                new UpdateUnitRequest
                {
                    Id = vm.Id,
                    Code =
                        vm.Code ??
                        string.Empty,
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
            ModelState.AddResultErrors(
                result);

            return View(vm);
        }

        TempData["ToastSuccess"] =
            "Đã cập nhật đơn vị.";

        return RedirectToAction(
            nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Unit.Update)]
    public async Task<IActionResult> ToggleStatus(
        int id,
        CancellationToken ct = default)
    {
        var storeId =
            CurrentStoreId;

        var result =
            await _service.ToggleStatusAsync(
                storeId,
                id,
                userId: null,
                ct);

        return Json(new
        {
            success =
                result.IsSuccess,

            message =
                result.IsSuccess
                    ? "Đã cập nhật trạng thái."
                    : result.Error.Message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Unit.Delete)]
    public async Task<IActionResult> DeleteAjax(
        int id,
        CancellationToken ct = default)
    {
        var storeId =
            CurrentStoreId;

        var result =
            await _service.SoftDeleteAsync(
                storeId,
                id,
                userId: null,
                ct);

        return Json(new
        {
            success =
                result.IsSuccess,

            message =
                result.IsSuccess
                    ? "Đã xóa đơn vị."
                    : result.Error.Message
        });
    }

    private static UnitIndexVM CreateIndexViewModel(
        string? search,
        bool? status,
        int page,
        int pageSize,
        PagedResult<UnitListItemDto> paged,
        (
            int TotalItems,
            int ActiveItems,
            int InactiveItems
        ) summary)
        => new()
        {
            SearchString = search,
            Status = status,
            Page = page,
            PageSize = pageSize,

            TotalUnitCount =
                summary.TotalItems,

            ActiveUnitCount =
                summary.ActiveItems,

            InactiveUnitCount =
                summary.InactiveItems,

            Paged = paged
        };
}