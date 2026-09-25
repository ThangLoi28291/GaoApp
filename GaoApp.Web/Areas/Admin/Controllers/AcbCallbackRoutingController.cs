using GaoApp.Application.Common.Security;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Authorize(Policy = PermissionCodes.System.Integration.Manage)]
[Route("admin/acb/callback-routing")]
public sealed class AcbCallbackRoutingController(AcbCallbackRoutingService routing) : BaseAdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!await routing.CanManageAsync(User, ct)) return Forbid();
        return View(await routing.PageAsync(User, ct));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(AcbCallbackRouteForm form, CancellationToken ct)
    {
        if (!await routing.CanManageAsync(User, ct)) return Forbid();
        if (!ModelState.IsValid) { ToastError("Hãy kiểm tra URL và cửa hàng đã chọn."); return RedirectToAction(nameof(Index)); }
        try
        {
            await routing.SaveAsync(User, form, ct);
            ToastSuccess(form.TargetStoreId.HasValue ? "Đã lưu cửa hàng nhận callback từ URL cũ." : "Đã tạm ngưng nhận callback tại URL cũ.");
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException error) { ToastError(error.Message); }
        catch (DbUpdateConcurrencyException) { ToastError("Cấu hình vừa được thay đổi. Hãy tải lại và kiểm tra trước khi lưu."); }
        catch (DbUpdateException) { ToastError("Chưa lưu được cấu hình. Hãy tải lại trang; nếu còn lỗi, kiểm tra kết nối và migration cơ sở dữ liệu."); }
        return RedirectToAction(nameof(Index));
    }
}
