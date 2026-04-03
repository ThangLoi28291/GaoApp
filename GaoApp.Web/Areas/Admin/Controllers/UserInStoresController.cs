using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Security.UserInStores;
using GaoApp.Application.Interfaces.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionCodes.Security.UserInStore.View)]
public class UserInStoresController : BaseAdminController
{
    private readonly IUserInStoreAdminService _userInStoreAdminService;
    private readonly ICurrentStorePermissionService _currentStorePermissionService;

    public UserInStoresController(
        IUserInStoreAdminService userInStoreAdminService,
        ICurrentStorePermissionService currentStorePermissionService)
    {
        _userInStoreAdminService = userInStoreAdminService;
        _currentStorePermissionService = currentStorePermissionService;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] UserInStoreIndexQueryDto query, CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;

        var vm = await _userInStoreAdminService.GetPagedAsync(storeId, query, ct);

        await LoadPagePermissionsAsync(storeId, userId, ct);
        ViewBag.RoleFilterOptions = vm.Roles
            .Select(x => new SelectListItem(x.DisplayText, x.RoleId.ToString(), x.RoleId == query.RoleId))
            .ToList();

        return View(vm);
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Security.UserInStore.Create)]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;

        await LoadPagePermissionsAsync(storeId, userId, ct);

        var roles = await _userInStoreAdminService.GetRoleLookupAsync(storeId, true, ct);
        ViewBag.RoleOptions = roles
            .Select(x => new SelectListItem(x.DisplayText, x.RoleId.ToString()))
            .ToList();

        ViewBag.UserOptions = new List<SelectListItem>();

        return View(new CreateUserInStoreRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Security.UserInStore.Create)]
    public async Task<IActionResult> Create(CreateUserInStoreRequest request, CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;

        await LoadPagePermissionsAsync(storeId, userId, ct);

        var roles = await _userInStoreAdminService.GetRoleLookupAsync(storeId, true, ct);
        ViewBag.RoleOptions = roles
            .Select(x => new SelectListItem(x.DisplayText, x.RoleId.ToString(), x.RoleId == request.RoleId))
            .ToList();

        var preloadUsers = await _userInStoreAdminService.SearchUsersAsync(null, 30, ct);
        ViewBag.UserOptions = preloadUsers
            .Select(x => new SelectListItem(x.DisplayText, x.UserId.ToString(), x.UserId == request.UserId))
            .ToList();

        if (!ModelState.IsValid)
            return View(request);

        var result = await _userInStoreAdminService.CreateAsync(storeId, userId, request, ct);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Tạo gán user vào store không thành công.");
            return View(request);
        }

        ToastSuccess("Thêm người dùng vào store thành công.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Security.UserInStore.Update)]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;

        await LoadPagePermissionsAsync(storeId, userId, ct);

        var dto = await _userInStoreAdminService.GetByIdAsync(storeId, id, ct);
        if (dto == null)
        {
            ToastError("Không tìm thấy bản ghi người dùng trong store.");
            return RedirectToAction(nameof(Index));
        }

        ViewBag.RoleOptions = dto.AvailableRoles
            .Select(x => new SelectListItem(x.DisplayText, x.RoleId.ToString(), x.RoleId == dto.RoleId))
            .ToList();

        ViewBag.MappingInfo = dto;

        var model = new UpdateUserInStoreRequest
        {
            Id = dto.Id,
            RoleId = dto.RoleId,
            IsActive = dto.IsActive
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Security.UserInStore.Update)]
    public async Task<IActionResult> Edit(UpdateUserInStoreRequest request, CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;

        await LoadPagePermissionsAsync(storeId, userId, ct);

        var dto = await _userInStoreAdminService.GetByIdAsync(storeId, request.Id, ct);
        if (dto == null)
        {
            ToastError("Không tìm thấy bản ghi người dùng trong store.");
            return RedirectToAction(nameof(Index));
        }

        ViewBag.RoleOptions = dto.AvailableRoles
            .Select(x => new SelectListItem(x.DisplayText, x.RoleId.ToString(), x.RoleId == request.RoleId))
            .ToList();

        ViewBag.MappingInfo = dto;

        if (!ModelState.IsValid)
            return View(request);

        var result = await _userInStoreAdminService.UpdateAsync(storeId, userId, request, ct);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Cập nhật không thành công.");
            return View(request);
        }

        ToastSuccess("Cập nhật người dùng trong store thành công.");

        var dtoAfterSave = await _userInStoreAdminService.GetByIdAsync(storeId, request.Id, ct);
        ViewBag.MappingInfo = dtoAfterSave;

        return View(request);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Security.UserInStore.Delete)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;

        var result = await _userInStoreAdminService.DeleteAsync(storeId, id, userId, ct);

        if (result.Success)
            ToastSuccess("Xóa người dùng khỏi store thành công.");
        else
            ToastError(result.ErrorMessage ?? "Xóa không thành công.");

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.Security.UserInStore.Create)]
    public async Task<IActionResult> SearchUsers(string? keyword, CancellationToken ct)
    {
        var users = await _userInStoreAdminService.SearchUsersAsync(keyword, 20, ct);

        return Json(users.Select(x => new
        {
            id = x.UserId,
            text = x.DisplayText
        }));
    }

    private async Task LoadPagePermissionsAsync(int storeId, int userId, CancellationToken ct)
    {
        ViewBag.CanCreate = await _currentStorePermissionService.HasPermissionAsync(
            storeId, userId, PermissionCodes.Security.UserInStore.Create, ct);

        ViewBag.CanUpdate = await _currentStorePermissionService.HasPermissionAsync(
            storeId, userId, PermissionCodes.Security.UserInStore.Update, ct);

        ViewBag.CanDelete = await _currentStorePermissionService.HasPermissionAsync(
            storeId, userId, PermissionCodes.Security.UserInStore.Delete, ct);
    }
}