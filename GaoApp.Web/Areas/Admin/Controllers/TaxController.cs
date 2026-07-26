using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Taxes;
using GaoApp.Application.Interfaces.Services.Taxes;
using GaoApp.Web.Areas.Admin.ViewModels.Taxes;
using Microsoft.AspNetCore.Mvc;

using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Web.Areas.Admin.Controllers;

public class TaxController : BaseAdminController
{
    private readonly ITaxService _service;

    public TaxController(
        ITaxService service)
    {
        _service = service;
    }


    private static void NormalizePagination(ref int page, ref int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;
    }

    // LIST
    [HttpGet]
    public async Task<IActionResult> Index(string? search = "", int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        NormalizePagination(ref page, ref pageSize);

        var storeId = CurrentStoreId;
        var paged = await _service.GetPagedAsync(storeId, search, page, pageSize, ct);

        return View(new TaxIndexVM
        {
            SearchString = search,
            Page = page,
            PageSize = pageSize,
            Paged = paged
        });
    }

    // AJAX TABLE
    [HttpGet]
    public async Task<IActionResult> Search(string? search = "", int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        NormalizePagination(ref page, ref pageSize);

        var storeId = CurrentStoreId;
        var paged = await _service.GetPagedAsync(storeId, search, page, pageSize, ct);

        return PartialView("_TaxTable", new TaxIndexVM
        {
            SearchString = search,
            Page = page,
            PageSize = pageSize,
            Paged = paged
        });
    }

    // CREATE
    [HttpGet]
    public IActionResult Create()
        => View("Edit", new TaxEditViewModel { Status = true });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TaxEditViewModel vm, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return View("Edit", vm);

        var storeId = CurrentStoreId;
        try
        {
            var dto = new CreateTaxRequest
            {
                Code = vm.Code,
                Name = vm.Name,
                Rate = vm.Rate,
                Status = vm.Status
            };

            await _service.CreateAsync(storeId, dto, userId: null, ct);
            TempData["ToastSuccess"] = "Đã tạo thuế.";
            return RedirectToAction(nameof(Index));
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError("", ex.SafeMessage);
            return View("Edit", vm);
        }
    }

    // EDIT
    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct = default)
    {
        var storeId = CurrentStoreId;
        var dto = await _service.GetForEditAsync(storeId, id, ct);
        if (dto == null) return NotFound();

        var vm = new TaxEditViewModel
        {
            Id = dto.Id,
            Code = dto.Code,
            Name = dto.Name,
            Rate = dto.Rate,
            Status = dto.Status,
            RowVersion = dto.RowVersion
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(TaxEditViewModel vm, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return View(vm);

        var storeId = CurrentStoreId;
        try
        {
            var dto = new UpdateTaxRequest
            {
                Id = vm.Id,
                Code = (vm.Code ?? "").Trim(),
                Name = vm.Name,
                Rate = vm.Rate,
                Status = vm.Status,
                RowVersion = vm.RowVersion
            };

            var ok = await _service.UpdateAsync(storeId, dto, userId: null, ct);
            if (!ok) return NotFound();

            TempData["ToastSuccess"] = "Đã cập nhật thuế.";
            return RedirectToAction(nameof(Index));
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError("", ex.SafeMessage);
            return View(vm);
        }
    }

    // TOGGLE ACTIVE (AJAX)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(int id, CancellationToken ct = default)
    {
        try
        {
            var storeId = CurrentStoreId;
            var ok = await _service.ToggleStatusAsync(storeId, id, userId: null, ct);

            if (!ok) return Json(new { success = false, message = "Không tìm thấy thuế." });
            return Json(new { success = true, message = "Đã cập nhật trạng thái." });
        }
        catch (BusinessRuleException ex)
        {
            return Json(new { success = false, message = ex.SafeMessage });
        }
    }

    // SOFT DELETE (AJAX)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAjax(int id, CancellationToken ct = default)
    {
        try
        {
            var storeId = CurrentStoreId;
            var ok = await _service.SoftDeleteAsync(storeId, id, userId: null, ct);

            if (!ok) return Json(new { success = false, message = "Không tìm thấy thuế." });
            return Json(new { success = true, message = "Đã xóa thuế." });
        }
        catch (BusinessRuleException ex)
        {
            return Json(new { success = false, message = ex.SafeMessage });
        }
    }
}
