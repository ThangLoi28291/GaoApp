using System.Security.Claims;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Customers;
using GaoApp.Application.Interfaces.Services.Customers;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Web.Areas.Admin.ViewModels.Customers;
using GaoApp.Web.Common.Extensions;
using GaoApp.Web.Common.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public sealed class CustomerController : Controller
{
    private readonly ICustomerManagementService _service;
    private readonly ICurrentStorePermissionService _permissionService;
    private readonly ITenantContext _tenantContext;

    public CustomerController(
        ICustomerManagementService service,
        ICurrentStorePermissionService permissionService,
        ITenantContext tenantContext)
    {
        _service = service;
        _permissionService = permissionService;
        _tenantContext = tenantContext;
    }

    [Authorize(Policy = PermissionCodes.Catalog.Customer.View)]
    [HttpGet]
    public async Task<IActionResult> Index(
        string? searchString = null,
        string? priceTier = null,
        bool? status = null,
        bool? haveDebt = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var vm = await BuildViewModelAsync(searchString, priceTier, status, haveDebt, page, pageSize, ct);
        await SetPermissionFlagsAsync(ct);
        ViewData["Title"] = "Khách hàng";
        return View(vm);
    }

    [Authorize(Policy = PermissionCodes.Catalog.Customer.View)]
    [HttpGet]
    public async Task<IActionResult> Search(
        string? searchString = null,
        string? priceTier = null,
        bool? status = null,
        bool? haveDebt = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var vm = await BuildViewModelAsync(searchString, priceTier, status, haveDebt, page, pageSize, ct);
        await SetPermissionFlagsAsync(ct);
        return PartialView("_CustomerTable", vm);
    }

    [Authorize(Policy = PermissionCodes.Catalog.Customer.View)]
    [HttpGet]
    public async Task<IActionResult> QuickView(int id, CancellationToken ct = default)
    {
        var customer = await _service.GetQuickViewAsync(id, ct);
        return customer is null ? NotFound() : Json(customer);
    }

    [Authorize(Policy = PermissionCodes.Catalog.Customer.Create)]
    [HttpGet]
    public IActionResult Create()
    {
        ViewData["Title"] = "Thêm khách hàng";
        return View("Edit", new CustomerEditDto());
    }

    [Authorize(Policy = PermissionCodes.Catalog.Customer.Create)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CustomerEditDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return View("Edit", dto);
        var result = await _service.CreateAsync(dto, ct);
        if (result.IsFailure)
        {
            ModelState.AddResultErrors(result);
            return View("Edit", dto);
        }

        TempData["ToastSuccess"] = "Tạo khách hàng thành công.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PermissionCodes.Catalog.Customer.Update)]
    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct = default)
    {
        var result = await _service.GetForEditAsync(id, ct);
        if (result.IsFailure)
        {
            TempData["ToastError"] = result.Error.Message;
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = "Cập nhật khách hàng";
        return View(result.Value);
    }

    [Authorize(Policy = PermissionCodes.Catalog.Customer.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CustomerEditDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return View(dto);
        var result = await _service.UpdateAsync(dto, ct);
        if (result.IsFailure)
        {
            ModelState.AddResultErrors(result);
            return View(dto);
        }

        TempData["ToastSuccess"] = "Cập nhật khách hàng thành công.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PermissionCodes.Catalog.Customer.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id, CancellationToken ct = default)
    {
        Result<bool> result = await _service.ToggleActiveAsync(id, ct);
        return Json(AjaxResponse.FromResult(result, "Đã cập nhật trạng thái khách hàng."));
    }

    private async Task<CustomerIndexVM> BuildViewModelAsync(
        string? searchString,
        string? priceTier,
        bool? status,
        bool? haveDebt,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var request = new CustomerManagementQueryRequest
        {
            SearchString = searchString,
            PriceTier = priceTier,
            Status = status,
            HaveDebt = haveDebt,
            Page = page,
            PageSize = pageSize
        };
        var result = await _service.GetPageAsync(request, ct);
        return new CustomerIndexVM
        {
            SearchString = request.SearchString ?? string.Empty,
            PriceTier = request.PriceTier,
            Status = request.Status,
            HaveDebt = request.HaveDebt,
            Page = result.Paged.Page,
            PageSize = result.Paged.PageSize,
            Summary = result.Summary,
            Paged = result.Paged
        };
    }

    private async Task SetPermissionFlagsAsync(CancellationToken ct)
    {
        var canCreate = _tenantContext.IsHostAdmin;
        var canUpdate = _tenantContext.IsHostAdmin;
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!canCreate && _tenantContext.StoreId is > 0 && int.TryParse(claim, out var userId))
        {
            canCreate = await _permissionService.HasPermissionAsync(
                _tenantContext.StoreId.Value, userId, PermissionCodes.Catalog.Customer.Create, ct);
            canUpdate = await _permissionService.HasPermissionAsync(
                _tenantContext.StoreId.Value, userId, PermissionCodes.Catalog.Customer.Update, ct);
        }

        ViewData["CanCreateCustomer"] = canCreate;
        ViewData["CanUpdateCustomer"] = canUpdate;
    }
}
