using GaoApp.Web.Security;
using System.Security.Claims;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Categories;
using GaoApp.Application.Interfaces.Services.Categories;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Web.Areas.Admin.ViewModels.Categories;
using GaoApp.Web.Common.Extensions;
using GaoApp.Web.Common.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

/// <summary>
/// Quản lý danh mục.
/// 
/// Giai đoạn A.1:
/// - dùng permission convention mới
/// - tách quyền theo CRUD rõ ràng
/// - hỗ trợ partial AJAX luôn nhận đúng cờ permission cho UI
/// - create/edit dùng TempData toast sau redirect
/// </summary>
[Area("Admin")]
[Authorize]
public class CategoryController : Controller
{
    private readonly ICategoryService _service;
    private readonly ICurrentStorePermissionService _permissionService;
    private readonly ITenantContext _tenantContext;

    public CategoryController(
        ICategoryService service,
        ICurrentStorePermissionService permissionService,
        ITenantContext tenantContext)
    {
        _service = service;
        _permissionService = permissionService;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Chuẩn hóa tham số phân trang để tránh request xấu hoặc quá lớn.
    /// </summary>
    private static void NormalizePagination(ref int page, ref int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;
    }

    /// <summary>
    /// Lấy userId hiện tại từ claim đăng nhập.
    /// Trả null nếu không parse được.
    /// </summary>
    private int? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(userIdClaim, out var userId) && userId > 0)
            return userId;

        return null;
    }

    /// <summary>
    /// Tính các cờ quyền cho UI hiện tại.
    /// </summary>
    private async Task SetCategoryPermissionFlagsAsync(CancellationToken ct = default)
    {
        bool canCreate = false;
        bool canUpdate = false;
        bool canDelete = false;

        // Host admin: có toàn quyền ở tầng UI để đồng bộ với PermissionAuthorizationHandler.
        if (_tenantContext.IsHostAdmin)
        {
            canCreate = true;
            canUpdate = true;
            canDelete = true;
        }
        else if (_tenantContext.StoreId.HasValue && _tenantContext.StoreId.Value > 0)
        {
            var userId = GetCurrentUserId();

            if (userId.HasValue)
            {
                var storeId = _tenantContext.StoreId.Value;

                canCreate = await _permissionService.HasPermissionAsync(
                    storeId,
                    userId.Value,
                    PermissionCodes.Catalog.Category.Create,
                    ct);

                canUpdate = await _permissionService.HasPermissionAsync(
                    storeId,
                    userId.Value,
                    PermissionCodes.Catalog.Category.Update,
                    ct);

                canDelete = await _permissionService.HasPermissionAsync(
                    storeId,
                    userId.Value,
                    PermissionCodes.Catalog.Category.Delete,
                    ct);
            }
        }

        ViewData["CanCreateCategory"] = canCreate;
        ViewData["CanUpdateCategory"] = canUpdate;
        ViewData["CanDeleteCategory"] = canDelete;
    }

    /// <summary>
    /// Màn hình danh sách danh mục.
    /// Yêu cầu quyền View.
    /// </summary>
    [Authorize(Policy = PermissionCodes.Catalog.Category.View)]
    [HttpGet]
    public async Task<IActionResult> Index(
        string? searchString = "",
        bool? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        NormalizePagination(ref page, ref pageSize);

        var paged = await _service.GetPagedAsync(searchString, status, page, pageSize, ct);
        var summary = await _service.GetSummaryAsync(ct);

        var vm = new CategoryIndexVM
        {
            SearchString = searchString ?? "",
            Status = status,
            Page = page,
            PageSize = pageSize,
            TotalCategoryCount = summary.TotalItems,
            ActiveCategoryCount = summary.ActiveItems,
            InactiveCategoryCount = summary.InactiveItems,
            Paged = paged
        };

        await SetCategoryPermissionFlagsAsync(ct);

        ViewData["Title"] = "Danh mục";
        return View(vm);
    }

    /// <summary>
    /// Partial search cho bảng danh mục.
    /// Yêu cầu quyền View.
    /// </summary>
    [Authorize(Policy = PermissionCodes.Catalog.Category.View)]
    [HttpGet]
    public async Task<IActionResult> Search(
        string? searchString = "",
        bool? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        NormalizePagination(ref page, ref pageSize);

        var paged = await _service.GetPagedAsync(searchString, status, page, pageSize, ct);
        var summary = await _service.GetSummaryAsync(ct);

        var vm = new CategoryIndexVM
        {
            SearchString = searchString ?? "",
            Status = status,
            Page = page,
            PageSize = pageSize,
            TotalCategoryCount = summary.TotalItems,
            ActiveCategoryCount = summary.ActiveItems,
            InactiveCategoryCount = summary.InactiveItems,
            Paged = paged
        };

        await SetCategoryPermissionFlagsAsync(ct);

        return PartialView("_CategoryTable", vm);
    }

    /// <summary>
    /// Mở form tạo mới danh mục.
    /// Yêu cầu quyền Create.
    /// </summary>
    [Authorize(Policy = PermissionCodes.Catalog.Category.Create)]
    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct = default)
    {
        ViewData["Title"] = "Thêm danh mục";
        ViewBag.ParentOptions = await _service.GetParentOptionsAsync(null, ct);

        return View("Edit", new CategoryEditDto
        {
            Id = 0,
            IsActive = true
        });
    }

    /// <summary>
    /// Submit tạo mới danh mục.
    /// Yêu cầu quyền Create.
    /// </summary>
    [Authorize(Policy = PermissionCodes.Catalog.Category.Create)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CategoryEditDto dto,
        CancellationToken ct = default)
    {
        ViewBag.ParentOptions = await _service.GetParentOptionsAsync(null, ct);

        if (!ModelState.IsValid)
            return View("Edit", dto);

        var result = await _service.CreateAsync(dto, ct);

        if (result.IsFailure)
        {
            ModelState.AddResultErrors(result);
            return View("Edit", dto);
        }

        TempData["ToastSuccess"] = "Tạo danh mục thành công.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Mở form cập nhật danh mục.
    /// Yêu cầu quyền Update.
    /// </summary>
    [Authorize(Policy = PermissionCodes.Catalog.Category.Update)]
    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct = default)
    {
        var result = await _service.GetForEditAsync(id, ct);

        if (result.IsFailure)
        {
            TempData["ToastError"] = result.Error.Message;
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = "Cập nhật danh mục";
        ViewBag.ParentOptions = await _service.GetParentOptionsAsync(id, ct);

        return View(result.Value);
    }

    /// <summary>
    /// Submit cập nhật danh mục.
    /// Yêu cầu quyền Update.
    /// </summary>
    [Authorize(Policy = PermissionCodes.Catalog.Category.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        CategoryEditDto dto,
        CancellationToken ct = default)
    {
        ViewBag.ParentOptions = await _service.GetParentOptionsAsync(dto.Id, ct);

        if (!ModelState.IsValid)
            return View(dto);

        var result = await _service.UpdateAsync(dto, ct);

        if (result.IsFailure)
        {
            ModelState.AddResultErrors(result);
            return View(dto);
        }

        TempData["ToastSuccess"] = "Cập nhật danh mục thành công.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Bật/tắt trạng thái hoạt động.
    /// Bản chất là update => dùng quyền Update.
    /// </summary>
    [Authorize(Policy = PermissionCodes.Catalog.Category.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(int id, CancellationToken ct = default)
    {
        var result = await _service.ToggleStatusAsync(id, ct);

        return Json(AjaxResponse.FromResult(result, "Đổi trạng thái thành công."));
    }

    /// <summary>
    /// Xóa mềm danh mục bằng AJAX.
    /// Yêu cầu quyền Delete.
    /// </summary>
    [Authorize(Policy = PermissionCodes.Catalog.Category.Delete)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAjax(int id, CancellationToken ct = default)
    {
        var result = await _service.DeleteAsync(id, ct);

        return Json(AjaxResponse.FromResult(result, "Xóa danh mục thành công."));
    }
    [Authorize(Policy = PermissionCodes.Catalog.Category.View)]

    public IActionResult Test403()
    {
        throw new UnauthorizedAccessException("Không có quyền test.");
    }
}
