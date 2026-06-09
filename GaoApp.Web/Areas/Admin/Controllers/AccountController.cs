using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.DTOs.Auth;
using GaoApp.Application.DTOs.Security.UserInStores;
using GaoApp.Application.Interfaces.Repositories.POSTerminals;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Interfaces.Services.Auth;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Identity;
using GaoApp.Infrastructure.Tenant;
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
    private const string PosDeviceKeyCookieName = "POS_DEVICE_KEY";

    private readonly IAuthService _authService;
    private readonly IAuditLogService _auditLogService;
    private readonly IPOSTerminalRepository _terminalRepository;
    private readonly ICurrentStore _currentStore;

    public AccountController(
        IAuthService authService,
        IAuditLogService auditLogService,
        IPOSTerminalRepository terminalRepository,
        ICurrentStore currentStore)
    {
        _authService = authService;
        _auditLogService = auditLogService;
        _terminalRepository = terminalRepository;
        _currentStore = currentStore;
    }

    [HttpGet("login")]
    public async Task<IActionResult> Login(string? returnUrl = null, CancellationToken ct = default)
    {
        var vm = await BuildLoginVmAsync(returnUrl, ct);
        return View("~/Areas/Admin/Views/Account/Login.cshtml", vm);
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVm vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await RebuildLoginVmAsync(vm, ct);
            return View("~/Areas/Admin/Views/Account/Login.cshtml", vm);
        }

        try
        {
            var cookieDeviceKey = Request.Cookies[PosDeviceKeyCookieName];

            var result = await _authService.LoginAsync(new LoginRequest
            {
                UserName = vm.UserName,
                Password = vm.Password,
                ReturnUrl = vm.ReturnUrl,

                DeviceKey = cookieDeviceKey,
                SelectedTerminalId = vm.SelectedTerminalId,
                DeviceName = vm.DeviceName,

                // Nếu LoginRequest chưa có UserAgent thì bỏ dòng này.
                UserAgent = Request.Headers.UserAgent.ToString()
            }, ct);

            if (!string.IsNullOrWhiteSpace(result.DeviceKey))
            {
                Response.Cookies.Append(
                    PosDeviceKeyCookieName,
                    result.DeviceKey,
                    new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        Expires = DateTimeOffset.UtcNow.AddYears(5)
                    });
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, result.UserId.ToString()),
                new Claim(ClaimTypes.Name, result.UserName),

                new Claim("user_name", result.UserName),
                new Claim("full_name", result.FullName ?? string.Empty),

                new Claim("store_id", result.StoreId.ToString()),
                new Claim("terminal_id", result.TerminalId.ToString()),
                new Claim("terminal_name", result.TerminalName ?? string.Empty),
                new Claim("terminal_code", result.TerminalCode ?? string.Empty),

                new Claim("role_id", result.RoleId.ToString()),
                new Claim("role_code", result.RoleCode ?? string.Empty),
                new Claim(ClaimTypes.Role, result.RoleCode ?? string.Empty)
            };

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

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

            return Redirect("/admin");
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

            await RebuildLoginVmAsync(vm, ct);
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
    public IActionResult AccessDenied(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View("~/Areas/Admin/Views/Account/AccessDenied.cshtml");
    }

    private async Task<LoginVm> BuildLoginVmAsync(string? returnUrl, CancellationToken ct)
    {
        var vm = new LoginVm
        {
            ReturnUrl = returnUrl
        };

        await RebuildLoginVmAsync(vm, ct);
        return vm;
    }

    private async Task RebuildLoginVmAsync(LoginVm vm, CancellationToken ct)
    {
        var storeId = _currentStore.StoreId;

        var terminals = await _terminalRepository.GetActiveByStoreAsync(storeId, ct);
        vm.AvailableTerminals = terminals;

        var deviceKey = Request.Cookies[PosDeviceKeyCookieName];

        POSTerminal? currentTerminal = null;

        if (!string.IsNullOrWhiteSpace(deviceKey))
        {
            currentTerminal = await _terminalRepository.GetByDeviceKeyAsync(storeId, deviceKey, ct);
        }

        vm.RequireTerminalPairing = currentTerminal == null;

        ViewBag.CurrentStoreName = HttpContext.Items["CurrentStoreName"]?.ToString() ?? "Cửa hàng hiện tại";

        if (currentTerminal != null)
        {
            ViewBag.CurrentTerminalName = currentTerminal.Name;
            ViewBag.CurrentTerminalCode = currentTerminal.Code;
        }
        else
        {
            ViewBag.CurrentTerminalName = "Thiết bị chưa ghép POS";
            ViewBag.CurrentTerminalCode = "";
        }
    }
    
}