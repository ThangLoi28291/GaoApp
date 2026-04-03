using GaoApp.Application.DTOs.Inventory.Warehouse;
using GaoApp.Application.Interfaces.Services.Inventory;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/api/warehouses")]
[ApiController]
public class WarehousesController : ControllerBase
{
    private readonly IWarehouseService _warehouseService;

    public WarehousesController(IWarehouseService warehouseService)
    {
        _warehouseService = warehouseService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await _warehouseService.GetAllAsync(ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWarehouseRequest request, CancellationToken ct)
    {
        var result = await _warehouseService.CreateAsync(request, ct);
        return Ok(result);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateWarehouseRequest request, CancellationToken ct)
    {
        var result = await _warehouseService.UpdateAsync(request, ct);
        return Ok(result);
    }

    [HttpGet("select2")]
    public async Task<IActionResult> Select2([FromQuery] string? term, CancellationToken ct)
    {
        var items = await _warehouseService.SearchSelect2Async(term, ct);

        return Ok(new
        {
            results = items.Select(x => new
            {
                id = x.Id,
                text = x.Text
            })
        });
    }
}