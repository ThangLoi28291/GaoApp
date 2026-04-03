using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/stock-counts")]
public class StockCountPagesController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Phiếu kiểm kê kho";
        return View();
    }

    [HttpGet("{id:int}")]
    public IActionResult Detail(int id)
    {
        ViewData["Title"] = "Chi tiết phiếu kiểm kê";
        ViewBag.StockCountDocumentId = id;
        return View();
    }
}