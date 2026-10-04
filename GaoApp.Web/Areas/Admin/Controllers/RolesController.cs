using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Security.Roles;
using GaoApp.Application.Interfaces.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionCodes.Security.Role.View)]
public class RolesController : BaseAdminController
{
    private readonly IRoleAdminService _roleAdminService;
    private readonly IRoleIndexReadService _roleIndexReadService;
    private readonly ICurrentStorePermissionService _currentStorePermissionService;

    public RolesController(
        IRoleAdminService roleAdminService,
        IRoleIndexReadService roleIndexReadService,
        ICurrentStorePermissionService currentStorePermissionService)
    {
        _roleAdminService = roleAdminService;
        _roleIndexReadService = roleIndexReadService;
        _currentStorePermissionService = currentStorePermissionService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;

        await LoadPagePermissionsAsync(storeId, userId, ct);

        return View();
    }

    [HttpGet("/admin/roles/data")]
    public async Task<IActionResult> GetRoleIndexData(
        [FromQuery] RoleIndexQueryRequest request,
        CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;
        var result = await _roleIndexReadService.GetPageAsync(storeId, request, ct);

        result.CanUpdate = await _currentStorePermissionService.HasPermissionAsync(
            storeId, userId, PermissionCodes.Security.Role.Update, ct);
        result.CanDelete = await _currentStorePermissionService.HasPermissionAsync(
            storeId, userId, PermissionCodes.Security.Role.Delete, ct);
        result.CanPermissions = await _currentStorePermissionService.HasPermissionAsync(
            storeId, userId, PermissionCodes.Security.Role.Permissions, ct);

        return Json(result);
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Security.Role.Create)]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;

        await LoadPagePermissionsAsync(storeId, userId, ct);

        return View(new RoleCreateRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Security.Role.Create)]
    public async Task<IActionResult> Create(RoleCreateRequest request, CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;

        await LoadPagePermissionsAsync(storeId, userId, ct);

        if (!ModelState.IsValid)
            return View(request);

        try
        {
            var newId = await _roleAdminService.CreateAsync(storeId, userId, request, ct);

            ToastSuccess("Tạo vai trò thành công.");
            if ((bool?)ViewBag.CanPermissions == true)
                return RedirectToAction("Index", "RolePermissions", new { roleId = newId });
            return RedirectToAction(nameof(Edit), new { id = newId });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(request);
        }
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Security.Role.Update)]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;

        await LoadPagePermissionsAsync(storeId, userId, ct);

        var dto = await _roleAdminService.GetByIdAsync(storeId, id, ct);
        if (dto == null)
        {
            ToastError("Không tìm thấy vai trò.");
            return RedirectToAction(nameof(Index));
        }

        ViewBag.RoleInfo = dto;

        var model = new RoleUpdateRequest
        {
            Id = dto.Id,
            Code = dto.Code,
            Name = dto.Name
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Security.Role.Update)]
    public async Task<IActionResult> Edit(RoleUpdateRequest request, CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;

        await LoadPagePermissionsAsync(storeId, userId, ct);

        if (!ModelState.IsValid)
        {
            ViewBag.RoleInfo = await _roleAdminService.GetByIdAsync(storeId, request.Id, ct);
            return View(request);
        }

        try
        {
            var ok = await _roleAdminService.UpdateAsync(storeId, userId, request, ct);

            if (!ok)
            {
                ToastError("Không tìm thấy vai trò.");
                return RedirectToAction(nameof(Index));
            }

            ToastSuccess("Cập nhật vai trò thành công.");
            ViewBag.RoleInfo = await _roleAdminService.GetByIdAsync(storeId, request.Id, ct);

            return View(request);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewBag.RoleInfo = await _roleAdminService.GetByIdAsync(storeId, request.Id, ct);
            return View(request);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Security.Role.Delete)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;

        var result = await _roleAdminService.DeleteAsync(storeId, id, userId, ct);

        if (result.Success)
            ToastSuccess("Xóa vai trò thành công.");
        else
            ToastError(result.ErrorMessage ?? "Xóa vai trò không thành công.");

        return RedirectToAction(nameof(Index));
    }

    private async Task LoadPagePermissionsAsync(int storeId, int userId, CancellationToken ct)
    {
        ViewBag.CanCreate = await _currentStorePermissionService.HasPermissionAsync(
            storeId, userId, PermissionCodes.Security.Role.Create, ct);

        ViewBag.CanUpdate = await _currentStorePermissionService.HasPermissionAsync(
            storeId, userId, PermissionCodes.Security.Role.Update, ct);

        ViewBag.CanDelete = await _currentStorePermissionService.HasPermissionAsync(
            storeId, userId, PermissionCodes.Security.Role.Delete, ct);

        ViewBag.CanPermissions = await _currentStorePermissionService.HasPermissionAsync(
            storeId, userId, PermissionCodes.Security.Role.Permissions, ct);
    }
}
