using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/stock-transfers")]
public class StockTransferController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Chuyển kho";
        return View();
    }

    [HttpGet("{id:int}")]
    public IActionResult Detail(int? id)
    {
        ViewData["Title"] = id.HasValue ? "Chi tiết phiếu chuyển kho" : "Tạo phiếu chuyển kho";
        ViewBag.StockTransferId = id;
        return View();
    }
}