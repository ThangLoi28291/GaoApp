// GaoApp.Web/Areas/Admin/Controllers/AdminMenusController.cs
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.AdminMenus;
using GaoApp.Application.Interfaces.Services.AdminMenus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionCodes.Security.Role.Permissions)]
public class AdminMenusController : BaseAdminController
{
    private readonly IAdminMenuService _adminMenuService;

    public AdminMenusController(IAdminMenuService adminMenuService)
    {
        _adminMenuService = adminMenuService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = await _adminMenuService.GetForAdminAsync(CurrentStoreId, ct);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? parentId, CancellationToken ct)
    {
        await LoadOptionsAsync(null, ct);

        return View(new SaveAdminMenuItemRequest
        {
            ParentId = parentId,
            Area = "Admin",
            Action = "Index",
            IsActive = true
        });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
    SaveAdminMenuItemRequest request,
    CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync(null, ct);
            return View(request);
        }

        try
        {
            await _adminMenuService.CreateAsync(
                CurrentStoreId,
                CurrentUserId,
                request,
                ct);

            ToastSuccess("Tạo menu thành công.");
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadOptionsAsync(null, ct);
            return View(request);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(
    int id,
    CancellationToken ct)
    {
        await LoadOptionsAsync(id, ct);

        var item = await _adminMenuService.GetByIdAsync(
            CurrentStoreId,
            id,
            ct);

        if (item == null)
        {
            ToastError("Không tìm thấy menu.");
            return RedirectToAction(nameof(Index));
        }

        // =====================================================
        // LOAD ROLE ĐÃ ĐƯỢC GÁN CHO PERMISSION
        // =====================================================

        var assignedRoleIds =
            await _adminMenuService.GetAssignedRoleIdsAsync(
                item.PermissionCode,
                ct);

        var vm = new SaveAdminMenuItemRequest
        {
            Id = item.Id,
            ParentId = item.ParentId,
            Title = item.Title,
            Area = item.Area,
            Controller = item.Controller,
            Action = item.Action,
            Url = item.Url,
            Icon = item.Icon,
            PermissionCode = item.PermissionCode,
            SortOrder = item.SortOrder,
            IsActive = item.IsActive,

            // =====================================================
            // QUAN TRỌNG
            // =====================================================

            AssignRoleIds = assignedRoleIds
        };

        return View(vm);
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
     SaveAdminMenuItemRequest request,
     CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync(request.Id, ct);
            return View(request);
        }

        try
        {
            var ok = await _adminMenuService.UpdateAsync(
                CurrentStoreId,
                CurrentUserId,
                request,
                ct);

            if (!ok)
            {
                ToastError("Không tìm thấy menu.");
                return RedirectToAction(nameof(Index));
            }

            ToastSuccess("Cập nhật menu thành công.");
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadOptionsAsync(request.Id, ct);
            return View(request);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await _adminMenuService.DeleteAsync(CurrentStoreId, id, CurrentUserId, ct);

        if (result.Success)
            ToastSuccess("Xóa menu thành công.");
        else
            ToastError(result.ErrorMessage ?? "Xóa menu không thành công.");

        return RedirectToAction(nameof(Index));
    }
    private async Task LoadOptionsAsync(CancellationToken ct)
    {
        var menus = await _adminMenuService.GetForAdminAsync(CurrentStoreId, ct);

        ViewBag.ParentMenus = menus;
    }
    private async Task LoadOptionsAsync(
    int? excludeMenuId,
    CancellationToken ct)
    {
        var options = await _adminMenuService.GetFormOptionsAsync(
            CurrentStoreId,
            excludeMenuId,
            ct);

        ViewBag.ParentMenus = options.ParentMenus;
        ViewBag.Permissions = options.Permissions;
        ViewBag.Roles = options.Roles;
    }
}