using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/inventory-adjustment-documents")]
[Authorize(Policy = PermissionCodes.Inventory.Adjustment.View)]
public class InventoryAdjustmentDocumentsController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Phiếu điều chỉnh kho";
        return View();
    }

    [HttpGet("create")]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.Create)]
    public IActionResult Create()
    {
        ViewData["Title"] = "Tạo phiếu điều chỉnh kho";
        return View();
    }

    [HttpGet("{id:int}")]
    public IActionResult Detail(int id)
    {
        ViewData["Title"] = "Chi tiết phiếu điều chỉnh kho";
        ViewBag.Id = id;
        return View();
    }

    [HttpGet("{id:int}/edit")]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.Update)]
    public IActionResult Edit(int id)
    {
        ViewData["Title"] = "Sửa phiếu điều chỉnh kho";
        ViewBag.Id = id;
        return View();
    }
}