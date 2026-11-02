using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GaoApp.Web.Areas.Admin.ViewModels.Commerce;

namespace GaoApp.Web.Areas.Admin.Controllers;

/// <summary>Read-only component preview. No menu entries or business data are created.</summary>
[Route("admin/ui-foundation")]
[Authorize(Policy = PermissionCodes.Security.Role.Permissions)]
public sealed class UiFoundationController : BaseAdminController
{
    [HttpGet("")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index() => View();

    [HttpGet("compare")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Compare() => View();

    [HttpGet("mobile")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Mobile() => View(MobilePreview.CreateDemo());
}
