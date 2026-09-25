using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GaoApp.Application.Common.Security;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/audit-logs")]
[Authorize(Policy = PermissionCodes.System.AuditLog.View)]
public class AuditLogsController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Audit log";
        return View();
    }
}
