using GaoApp.Application.Common;
using GaoApp.Application.DTOs.AttributeValues;
using GaoApp.Application.Interfaces.Services.AttributeValues;
using GaoApp.Application.Interfaces.Services.ProductAttributes;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Areas.Admin.ViewModels.AttributeValues;
using GaoApp.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public class AttributeValueController : BaseAdminController
{
    private readonly IAttributeValueService _service;
    private readonly IProductAttributeService _attrService;
    private readonly ITenantContext _tenant;

    public AttributeValueController(
        IAttributeValueService service,
        IProductAttributeService attrService,
        ITenantContext tenant)
    {
        _service = service;
        _attrService = attrService;
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
    public async Task<IActionResult> Index(int? attributeId = null, string? search = "", int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        NormalizePagination(ref page, ref pageSize);

        var storeId = CurrentStoreId();
        var attrs = await _attrService.GetAllAsync(storeId, ct);
        var paged = await _service.GetPagedAsync(storeId, attributeId, search, page, pageSize, ct);

        return View(new AttributeValueIndexVM
        {
            AttributeId = attributeId,
            SearchString = search,
            Page = page,
            PageSize = pageSize,
            Attributes = attrs,
            Paged = paged
        });
    }

    [HttpGet]
    public async Task<IActionResult> Search(int? attributeId = null, string? search = "", int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        NormalizePagination(ref page, ref pageSize);

        var storeId = CurrentStoreId();
        var attrs = await _attrService.GetAllAsync(storeId, ct);
        var paged = await _service.GetPagedAsync(storeId, attributeId, search, page, pageSize, ct);

        return PartialView("_AttributeValueTable", new AttributeValueIndexVM
        {
            AttributeId = attributeId,
            SearchString = search,
            Page = page,
            PageSize = pageSize,
            Attributes = attrs,
            Paged = paged
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct = default)
    {
        var storeId = CurrentStoreId();
        var attrs = await _attrService.GetAllAsync(storeId, ct);

        return View("Edit", new AttributeValueEditViewModel
        {
            Status = true,
            Attributes = attrs
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AttributeValueEditViewModel vm, CancellationToken ct = default)
    {
        var storeId = CurrentStoreId();
        vm.Attributes = await _attrService.GetAllAsync(storeId, ct);

        if (!ModelState.IsValid)
            return View("Edit", vm);

        var result = await _service.CreateAsync(
            storeId,
            new CreateAttributeValueRequest
            {
                AttributeId = vm.AttributeId,
                Code = vm.Code,
                Name = vm.Name,
                Status = vm.Status
            },
            userId: null,
            ct);

        if (!result.IsSuccess)
        {
            ModelState.AddResultErrors(result);
            return View("Edit", vm);
        }

        TempData["ToastSuccess"] = "Đã tạo giá trị thuộc tính.";
        return RedirectToAction(nameof(Index), new { attributeId = vm.AttributeId });
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

        var attrs = await _attrService.GetAllAsync(storeId, ct);
        var dto = result.Value;

        return View(new AttributeValueEditViewModel
        {
            Id = dto.Id,
            AttributeId = dto.AttributeId,
            Code = dto.Code,
            Name = dto.Name,
            Status = dto.Status,
            RowVersion = dto.RowVersion,
            Attributes = attrs
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AttributeValueEditViewModel vm, CancellationToken ct = default)
    {
        var storeId = CurrentStoreId();
        vm.Attributes = await _attrService.GetAllAsync(storeId, ct);

        if (!ModelState.IsValid)
            return View(vm);

        var result = await _service.UpdateAsync(
            storeId,
            new UpdateAttributeValueRequest
            {
                Id = vm.Id,
                AttributeId = vm.AttributeId,
                Code = vm.Code ?? string.Empty,
                Name = vm.Name,
                Status = vm.Status,
                RowVersion = vm.RowVersion
            },
            userId: null,
            ct);

        if (!result.IsSuccess)
        {
            ModelState.AddResultErrors(result);
            return View(vm);
        }

        TempData["ToastSuccess"] = "Đã cập nhật giá trị.";
        return RedirectToAction(nameof(Index), new { attributeId = vm.AttributeId });
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
            message = result.IsSuccess ? "Đã xóa giá trị." : result.Error.Message
        });
    }
}