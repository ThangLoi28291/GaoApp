using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.DTOs.Auth;
using GaoApp.Application.Common.Security;
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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IAuthService authService,
        IAuditLogService auditLogService,
        IPOSTerminalRepository terminalRepository,
        ICurrentStore currentStore,
        ILogger<AccountController> logger)
    {
        _authService = authService;
        _auditLogService = auditLogService;
        _terminalRepository = terminalRepository;
        _currentStore = currentStore;
        _logger = logger;
    }

    [HttpGet("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl = null, CancellationToken ct = default, bool manageTerminals = false)
    {
        var vm = await BuildLoginVmAsync(returnUrl, ct);
        vm.ManageTerminals = manageTerminals || returnUrl?.StartsWith("/admin/pos-terminals", StringComparison.OrdinalIgnoreCase) == true;
        return View("~/Areas/Admin/Views/Account/Login.cshtml", vm);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequestSizeLimit(64 * 1024)]
    [ServiceFilter(typeof(GaoApp.Web.Security.LoginAccountRateLimitFilter))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVm vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await RebuildLoginVmAsync(vm, ct);
            return View("~/Areas/Admin/Views/Account/Login.cshtml", vm);
        }

        var cookieDeviceKey = Request.Cookies[PosDeviceKeyCookieName];

        var loginResult = await _authService.LoginAsync(new LoginRequest
        {
            UserName = vm.UserName,
            ManageTerminals = vm.ManageTerminals,
            PairingKey = vm.PairingKey,
            Password = vm.Password,
            ReturnUrl = vm.ReturnUrl,
            DeviceKey = cookieDeviceKey,
            SelectedTerminalId = vm.SelectedTerminalId,
            DeviceName = vm.DeviceName,
            UserAgent = Request.Headers.UserAgent.ToString()
        }, ct);

        if (loginResult.IsFailure)
        {
            ModelState.AddModelError(
                string.Empty,
                loginResult.Error.Message);

            await TryWriteAuditAsync(new WriteAuditLogRequest
            {
                Module = AuditModuleType.Authentication,
                ActionType = AuditActionType.Login,
                Summary = "Đăng nhập thất bại.",
                IsSuccess = false
            }, "LoginFailure", ct);

            await RebuildLoginVmAsync(vm, ct);
            return View("~/Areas/Admin/Views/Account/Login.cshtml", vm);
        }

        var result = loginResult.Value;

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
            new(AuthSessionStamp.ClaimType, result.SessionStamp),
            new(ClaimTypes.NameIdentifier, result.UserId.ToString()),
            new(ClaimTypes.Name, result.UserName),
            new("user_name", result.UserName),
            new("full_name", result.FullName ?? string.Empty),
            new("store_id", result.StoreId.ToString()),
            new("terminal_id", result.TerminalId.ToString()),
            new("terminal_name", result.TerminalName ?? string.Empty),
            new("terminal_code", result.TerminalCode ?? string.Empty),
            new("role_id", result.RoleId.ToString()),
            new("role_code", result.RoleCode ?? string.Empty),
            new(ClaimTypes.Role, result.RoleCode ?? string.Empty)
        };

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        // Technical authentication-handler failures must reach the global owner.
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        await TryWriteAuditAsync(new WriteAuditLogRequest
        {
            StoreId = result.StoreId,
            Module = AuditModuleType.Authentication,
            ActionType = AuditActionType.Login,
            Summary = "Đăng nhập thành công.",
            IsSuccess = true
        }, "LoginSuccess", ct);

        if (vm.ManageTerminals)
            return Redirect("/admin/pos-terminals");

        if (!string.IsNullOrWhiteSpace(vm.ReturnUrl) &&
            Url.IsLocalUrl(vm.ReturnUrl))
        {
            return LocalRedirect(vm.ReturnUrl);
        }

        return Redirect("/admin");
    }

    [HttpPost("logout")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await TryWriteAuditAsync(new WriteAuditLogRequest
        {
            Module = AuditModuleType.Authentication,
            ActionType = AuditActionType.Logout,
            Summary = "Đăng xuất",
            IsSuccess = true
        }, "Logout", ct);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.RequestServices.GetRequiredService<GaoApp.Web.Hubs.RevocablePosHubLifetimeManager>()
            .RevokeSession(User);
        return Redirect("/admin/account/login");
    }

    [HttpGet("access-denied")]
    [AllowAnonymous]
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

    private async Task TryWriteAuditAsync(
        WriteAuditLogRequest request,
        string operation,
        CancellationToken ct)
    {
        try
        {
            await _auditLogService.WriteAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Authentication audit failed; primary authentication behavior is preserved. Operation={Operation}; TraceId={TraceId}; ExceptionType={ExceptionType}",
                operation,
                HttpContext.TraceIdentifier,
                ex.GetType().Name);
        }
    }
    
}
