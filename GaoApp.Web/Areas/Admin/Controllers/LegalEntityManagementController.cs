using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionCodes.System.LegalEntity.View)]
public sealed class LegalEntityManagementController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
