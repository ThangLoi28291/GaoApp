using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Domain.Enums;
using GaoApp.Web.Areas.Admin.ViewModels.Account;
using GaoApp.Web.Hubs;
using GaoApp.Web.Services.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/account")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EmployeeAccountController(EmployeeAccountService accounts, IAuditLogService audit,
    RevocablePosHubLifetimeManager sessions, ILogger<EmployeeAccountController> logger) : Controller
{
    [HttpPost("change-password")]
    [ValidateAntiForgeryToken]
    [ServiceFilter(typeof(GaoApp.Web.Security.SelfPasswordRateLimitFilter))]
    [RequestSizeLimit(8 * 1024)]
    public async Task<IActionResult> ChangePassword([FromForm] ChangeOwnPasswordVm model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationErrors();
        var outcome = await accounts.ChangePasswordAsync(User, model.CurrentPassword, model.NewPassword, ct);
        switch (outcome)
        {
            case PasswordChangeOutcome.InvalidSession:
                return Unauthorized(new { message = "Phiên đăng nhập đã thay đổi. Vui lòng đăng nhập lại." });
            case PasswordChangeOutcome.IncorrectPassword:
                ModelState.AddModelError(nameof(model.CurrentPassword), "Mật khẩu hiện tại chưa đúng.");
                return ValidationErrors();
            case PasswordChangeOutcome.ReusedPassword:
                ModelState.AddModelError(nameof(model.NewPassword), "Mật khẩu mới phải khác mật khẩu hiện tại.");
                return ValidationErrors();
            case PasswordChangeOutcome.Conflict:
                return Conflict(new { message = "Tài khoản vừa thay đổi ở nơi khác. Vui lòng đăng nhập lại trước khi thử tiếp." });
        }

        // All cookies become invalid through AuthSessionStamp; close this user's live POS connections as well.
        sessions.RevokeUser(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        try
        {
            await audit.WriteAsync(new WriteAuditLogRequest
            {
                Module = AuditModuleType.Authentication, ActionType = AuditActionType.Update,
                Summary = "Nhân viên tự đổi mật khẩu. Các phiên cũ cần đăng nhập lại.", IsSuccess = true
            }, ct);
        }
        catch (Exception ex)
        {
            // A completed credential change must not be presented as a failed change if its audit sink is unavailable.
            logger.LogWarning("Password change audit failed. TraceId={TraceId}; ExceptionType={ExceptionType}",
                HttpContext.TraceIdentifier, ex.GetType().Name);
        }
        TempData["PasswordChanged"] = "Đổi mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới.";
        return Ok(new { message = "Đổi mật khẩu thành công.", redirectUrl = Url.Action("Login", "Account", new { area = "Admin" }) });
    }

    private BadRequestObjectResult ValidationErrors() => BadRequest(new
    {
        message = "Kiểm tra lại thông tin mật khẩu.",
        errors = ModelState.Where(x => x.Value?.Errors.Count > 0)
            .ToDictionary(x => x.Key, x => x.Value!.Errors.Select(e => e.ErrorMessage).ToArray())
    });
}
