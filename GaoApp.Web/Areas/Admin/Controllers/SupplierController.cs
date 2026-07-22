using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Suppliers;
using GaoApp.Application.Interfaces.Services.Suppliers;

using GaoApp.Web.Areas.Admin.ViewModels.Suppliers;
using GaoApp.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Authorize(Policy = PermissionCodes.Catalog.Supplier.View)]
public class SupplierController : BaseAdminController
{
    private readonly ISupplierService _service;

    public SupplierController(
        ISupplierService service)
    {
        _service = service;
    }


    private static void NormalizePagination(ref int page, ref int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search = "",
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        NormalizePagination(ref page, ref pageSize);

        var storeId = CurrentStoreId;
        var paged = await _service.GetPagedAsync(storeId, search, page, pageSize, ct);

        return View(new SupplierIndexVM
        {
            SearchString = search,
            Page = page,
            PageSize = pageSize,
            Paged = paged
        });
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        string? search = "",
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        NormalizePagination(ref page, ref pageSize);

        var storeId = CurrentStoreId;
        var paged = await _service.GetPagedAsync(storeId, search, page, pageSize, ct);

        return PartialView("_SupplierTable", new SupplierIndexVM
        {
            SearchString = search,
            Page = page,
            PageSize = pageSize,
            Paged = paged
        });
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Catalog.Supplier.Create)]
    public IActionResult Create()
        => View("Edit", new SupplierEditViewModel { Status = true });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Supplier.Create)]
    public async Task<IActionResult> Create(SupplierEditViewModel vm, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return View("Edit", vm);

        var storeId = CurrentStoreId;

        var result = await _service.CreateAsync(
            storeId,
            new CreateSupplierRequest
            {
                Code = vm.Code,
                Name = vm.Name,
                Phone = vm.Phone,
                Email = vm.Email,
                Address = vm.Address,
                ContactName = vm.ContactName,
                TaxCode = vm.TaxCode,
                Note = vm.Note,
                Status = vm.Status
            },
            userId: null,
            ct);

        if (result.IsFailure)
        {
            ModelState.AddResultErrors(result);
            return View("Edit", vm);
        }

        TempData["ToastSuccess"] = "Đã tạo nhà cung cấp.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Catalog.Supplier.Update)]
    public async Task<IActionResult> Edit(int id, CancellationToken ct = default)
    {
        var storeId = CurrentStoreId;
        var result = await _service.GetForEditAsync(storeId, id, ct);

        if (result.IsFailure)
        {
            TempData["ToastError"] = result.Error.Message;
            return RedirectToAction(nameof(Index));
        }

        var dto = result.Value;

        return View(new SupplierEditViewModel
        {
            Id = dto.Id,
            Code = dto.Code,
            Name = dto.Name,
            Phone = dto.Phone,
            Email = dto.Email,
            Address = dto.Address,
            ContactName = dto.ContactName,
            TaxCode = dto.TaxCode,
            Note = dto.Note,
            Status = dto.Status,
            RowVersion = dto.RowVersion
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Supplier.Update)]
    public async Task<IActionResult> Edit(SupplierEditViewModel vm, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return View(vm);

        var storeId = CurrentStoreId;

        var result = await _service.UpdateAsync(
            storeId,
            new UpdateSupplierRequest
            {
                Id = vm.Id,
                Code = vm.Code ?? string.Empty,
                Name = vm.Name,
                Phone = vm.Phone,
                Email = vm.Email,
                Address = vm.Address,
                ContactName = vm.ContactName,
                TaxCode = vm.TaxCode,
                Note = vm.Note,
                Status = vm.Status,
                RowVersion = vm.RowVersion
            },
            userId: null,
            ct);

        if (result.IsFailure)
        {
            ModelState.AddResultErrors(result);
            return View(vm);
        }

        TempData["ToastSuccess"] = "Đã cập nhật nhà cung cấp.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Supplier.Update)]
    public async Task<IActionResult> ToggleStatus(int id, CancellationToken ct = default)
    {
        var storeId = CurrentStoreId;
        var result = await _service.ToggleStatusAsync(storeId, id, userId: null, ct);

        return Json(new
        {
            success = result.IsSuccess,
            message = result.IsSuccess
                ? "Đã cập nhật trạng thái."
                : result.Error.Message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Catalog.Supplier.Delete)]
    public async Task<IActionResult> DeleteAjax(int id, CancellationToken ct = default)
    {
        var storeId = CurrentStoreId;
        var result = await _service.SoftDeleteAsync(storeId, id, userId: null, ct);

        return Json(new
        {
            success = result.IsSuccess,
            message = result.IsSuccess
                ? "Đã xóa nhà cung cấp."
                : result.Error.Message
        });
    }
}