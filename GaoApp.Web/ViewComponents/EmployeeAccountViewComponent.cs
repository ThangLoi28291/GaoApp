using GaoApp.Application.Common.Security;
using GaoApp.Web.Services.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.ViewComponents;

public sealed record EmployeeAccountMenuModel(EmployeeProfile Profile, bool CanUsePos);

public sealed class EmployeeAccountViewComponent(EmployeeAccountService accounts, IAuthorizationService authorization) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (User.Identity?.IsAuthenticated != true) return Content("");
        var profile = await accounts.ProfileAsync(UserClaimsPrincipal, HttpContext.RequestAborted);
        if (profile is null) return Content("");
        var canUsePos = (await authorization.AuthorizeAsync(UserClaimsPrincipal, PermissionCodes.Pos.Order.View)).Succeeded;
        HttpContext.Response.Headers.CacheControl = "no-store, private";
        return View("~/Areas/Admin/Views/Shared/Components/EmployeeAccount/Default.cshtml", new EmployeeAccountMenuModel(profile, canUsePos));
    }
}
