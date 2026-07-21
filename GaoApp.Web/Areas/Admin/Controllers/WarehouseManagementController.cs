using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionCodes.Inventory.Warehouse.View)]
public class WarehouseManagementController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}