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
    private readonly IEmployeeIndexReadService _employeeIndexReadService;
    private readonly ICurrentStorePermissionService _currentStorePermissionService;

    public UserInStoresController(
        IUserInStoreAdminService userInStoreAdminService,
        IEmployeeIndexReadService employeeIndexReadService,
        ICurrentStorePermissionService currentStorePermissionService)
    {
        _userInStoreAdminService = userInStoreAdminService;
        _employeeIndexReadService = employeeIndexReadService;
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

    [HttpGet("/admin/employees/data")]
    public async Task<IActionResult> GetEmployeeIndexData(
        [FromQuery] EmployeeIndexQueryRequest request,
        CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;
        var result = await _employeeIndexReadService.GetPageAsync(
            storeId,
            userId,
            request,
            ct);

        result.CanUpdate = await _currentStorePermissionService.HasPermissionAsync(
            storeId,
            userId,
            PermissionCodes.Security.UserInStore.Update,
            ct);

        return Json(result);
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
            IsActive = dto.IsActive,

            PhoneNumber = dto.PhoneNumber,
            PositionName = dto.PositionName,
            JoinedDate = dto.JoinedDate,
            Note = dto.Note
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
        ViewBag.CanAssignExistingUsers = await _userInStoreAdminService.CanAssignExistingUsersAsync(userId, ct);
        ViewBag.CanCreate = await _currentStorePermissionService.HasPermissionAsync(
            storeId, userId, PermissionCodes.Security.UserInStore.Create, ct);

        ViewBag.CanUpdate = await _currentStorePermissionService.HasPermissionAsync(
            storeId, userId, PermissionCodes.Security.UserInStore.Update, ct);

        ViewBag.CanDelete = await _currentStorePermissionService.HasPermissionAsync(
            storeId, userId, PermissionCodes.Security.UserInStore.Delete, ct);
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Security.UserInStore.Create)]
    public async Task<IActionResult> CreateEmployee(CreateEmployeeInStoreRequest request, CancellationToken ct)
    {
        var storeId = CurrentStoreId;
        var userId = CurrentUserId;

        if (!ModelState.IsValid)
        {
            ToastError("Dữ liệu tạo nhân viên chưa hợp lệ.");
            return RedirectToAction(nameof(Create));
        }

        var result = await _userInStoreAdminService.CreateEmployeeAsync(
            storeId,
            userId,
            request,
            ct);

        if (!result.Success)
        {
            ToastError(result.ErrorMessage ?? "Tạo nhân viên không thành công.");
            return RedirectToAction(nameof(Create));
        }

        ToastSuccess("Tạo nhân viên mới thành công.");
        return RedirectToAction(nameof(Index));
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Security.UserInStore.Update)]
    public async Task<IActionResult> ResetPassword(
    ResetPasswordRequest request,
    CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            ToastError("Mật khẩu không hợp lệ.");
            return RedirectToAction(nameof(Index));
        }

        var result = await _userInStoreAdminService.ResetPasswordAsync(
            CurrentStoreId,
            request.UserId,
            request.NewPassword,
            CurrentUserId,
            ct);

        if (!result.Success)
        {
            ToastError(result.ErrorMessage ?? "Không thể đổi mật khẩu.");
            return RedirectToAction(nameof(Index));
        }

        ToastSuccess("Đã đổi mật khẩu thành công.");
        return RedirectToAction(nameof(Index));
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Security.UserInStore.Update)]
    public async Task<IActionResult> ToggleActive(
    int id,
    CancellationToken ct)
    {
        var result = await _userInStoreAdminService.ToggleActiveAsync(
            CurrentStoreId,
            id,
            CurrentUserId,
            ct);

        if (!result.Success)
        {
            ToastError(result.ErrorMessage ?? "Không thể cập nhật trạng thái.");
            return RedirectToAction(nameof(Index));
        }

        ToastSuccess("Đã cập nhật trạng thái nhân viên.");
        return RedirectToAction(nameof(Index));
    }
}
