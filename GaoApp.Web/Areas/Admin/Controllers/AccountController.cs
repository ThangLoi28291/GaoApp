using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.DTOs.Auth;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Interfaces.Services.Auth;
using GaoApp.Domain.Enums;
using GaoApp.Web.Areas.Admin.ViewModels.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace GaoApp.Web.Controllers;

[Area("Admin")]
[Route("admin/account")]
public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly IAuditLogService _auditLogService;

    public AccountController(
        IAuthService authService,
        IAuditLogService auditLogService)
    {
        _authService = authService;
        _auditLogService = auditLogService;
    }

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.CurrentStoreName = HttpContext.Items["CurrentStoreName"]?.ToString();
        ViewBag.CurrentTerminalName = HttpContext.Items["CurrentTerminalName"]?.ToString();
        ViewBag.CurrentTerminalCode = HttpContext.Items["CurrentTerminalCode"]?.ToString();

        return View("~/Areas/Admin/Views/Account/Login.cshtml", new LoginVm
        {
            ReturnUrl = returnUrl
        });
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVm vm, CancellationToken ct)
    {
        void BindLoginContext()
        {
            ViewBag.CurrentStoreName = HttpContext.Items["CurrentStoreName"]?.ToString();
            ViewBag.CurrentTerminalName = HttpContext.Items["CurrentTerminalName"]?.ToString();
            ViewBag.CurrentTerminalCode = HttpContext.Items["CurrentTerminalCode"]?.ToString();
        }

        if (!ModelState.IsValid)
        {
            BindLoginContext();
            return View("~/Areas/Admin/Views/Account/Login.cshtml", vm);
        }

        try
        {
            var result = await _authService.LoginAsync(new LoginRequest
            {
                UserName = vm.UserName,
                Password = vm.Password,
                ReturnUrl = vm.ReturnUrl
            }, ct);

            var claims = new List<Claim>
            {
                // Identity chuẩn
                new Claim(ClaimTypes.NameIdentifier, result.UserId.ToString()),
                new Claim(ClaimTypes.Name, result.UserName),

                // Thông tin hiển thị
                new Claim("user_name", result.UserName),
                new Claim("full_name", result.FullName ?? string.Empty),

                // Store / terminal context
                new Claim("store_id", result.StoreId.ToString()),
                new Claim("terminal_id", result.TerminalId.ToString()),
                new Claim("terminal_name", result.TerminalName ?? string.Empty),
                new Claim("terminal_code", result.TerminalCode ?? string.Empty),

                // Nếu LoginResponse đã mở rộng role thì thêm các claim này
                // new Claim("role_id", result.RoleId.ToString()),
                // new Claim("role_code", result.RoleCode ?? string.Empty),
                // new Claim(ClaimTypes.Role, result.RoleCode ?? string.Empty),
            };

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal);

            await _auditLogService.WriteAsync(new WriteAuditLogRequest
            {
                StoreId = result.StoreId,
                Module = AuditModuleType.Authentication,
                ActionType = AuditActionType.Login,
                Summary = $"Đăng nhập: {result.UserName} - Terminal: {result.TerminalCode} - IP: {result.ClientIp}",
                IsSuccess = true
            });

            if (!string.IsNullOrWhiteSpace(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl))
                return LocalRedirect(vm.ReturnUrl);

            return Redirect("/admin/pos-shift");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);

            await _auditLogService.WriteAsync(new WriteAuditLogRequest
            {
                Module = AuditModuleType.Authentication,
                ActionType = AuditActionType.Login,
                Summary = $"Đăng nhập thất bại: {vm.UserName}",
                IsSuccess = false
            });

            BindLoginContext();
            return View("~/Areas/Admin/Views/Account/Login.cshtml", vm);
        }
    }

    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        await _auditLogService.WriteAsync(new WriteAuditLogRequest
        {
            Module = AuditModuleType.Authentication,
            ActionType = AuditActionType.Logout,
            Summary = "Đăng xuất",
            IsSuccess = true
        });

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/admin/account/login");
    }

    [HttpGet("access-denied")]
    public IActionResult AccessDenied()
    {
        return Content("Bạn không có quyền truy cập chức năng này.");
    }
}