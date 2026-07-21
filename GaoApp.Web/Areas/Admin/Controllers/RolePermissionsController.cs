using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Security.Permissions;
using GaoApp.Application.Interfaces.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionCodes.Security.Role.Permissions)]
public class RolePermissionsController : BaseAdminController
{
    private readonly IRolePermissionAdminService _rolePermissionAdminService;
    private readonly ICurrentStorePermissionService _currentStorePermissionService;

    public RolePermissionsController(
        IRolePermissionAdminService rolePermissionAdminService,
        ICurrentStorePermissionService currentStorePermissionService)
    {
        _rolePermissionAdminService = rolePermissionAdminService;
        _currentStorePermissionService = currentStorePermissionService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int roleId, CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;

        var vm = await _rolePermissionAdminService.GetMatrixAsync(storeId, roleId, ct);
        if (vm == null)
        {
            ToastError("Không tìm thấy vai trò.");
            return RedirectToAction("Index", "Roles", new { area = "Admin" });
        }

        await LoadPagePermissionsAsync(storeId, userId, ct);

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SaveRolePermissionsRequest request, CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;

        var result = await _rolePermissionAdminService.SaveAsync(storeId, userId, request, ct);

        if (result.Success)
            ToastSuccess("Lưu phân quyền thành công.");
        else
            ToastError(result.ErrorMessage ?? "Lưu phân quyền không thành công.");

        return RedirectToAction(nameof(Index), new { roleId = request.RoleId });
    }

    private async Task LoadPagePermissionsAsync(int storeId, int userId, CancellationToken ct)
    {
        ViewBag.CanRoleView = await _currentStorePermissionService.HasPermissionAsync(
            storeId, userId, PermissionCodes.Security.Role.View, ct);

        ViewBag.CanRoleUpdate = await _currentStorePermissionService.HasPermissionAsync(
            storeId, userId, PermissionCodes.Security.Role.Update, ct);

        ViewBag.CanRolePermissions = await _currentStorePermissionService.HasPermissionAsync(
            storeId, userId, PermissionCodes.Security.Role.Permissions, ct);
    }
}