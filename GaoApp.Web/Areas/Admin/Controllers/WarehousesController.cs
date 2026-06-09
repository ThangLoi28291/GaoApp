using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory.Warehouse;
using GaoApp.Application.Interfaces.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/api/warehouses")]
[Authorize]
[ApiController]
public class WarehousesController : ControllerBase
{
    private readonly IWarehouseService _warehouseService;

    public WarehousesController(IWarehouseService warehouseService)
    {
        _warehouseService = warehouseService;
    }
    public class ToggleWarehouseFlagRequest
    {
        public bool Value { get; set; }
    }
    [HttpPatch("{id:int}/allow-negative")]
    [Authorize(Policy = PermissionCodes.Inventory.Warehouse.Update)]
    public async Task<IActionResult> ToggleAllowNegative(
    int id,
    [FromBody] ToggleWarehouseFlagRequest request,
    CancellationToken ct)
    {
        var result = await _warehouseService.ToggleAllowNegativeInventoryAsync(id, request.Value, ct);
        return Ok(result);
    }

    [HttpPatch("{id:int}/active")]
    [Authorize(Policy = PermissionCodes.Inventory.Warehouse.Update)]
    public async Task<IActionResult> ToggleActive(
        int id,
        [FromBody] ToggleWarehouseFlagRequest request,
        CancellationToken ct)
    {
        var result = await _warehouseService.ToggleActiveAsync(id, request.Value, ct);
        return Ok(result);
    }

    [HttpPatch("{id:int}/default")]
    [Authorize(Policy = PermissionCodes.Inventory.Warehouse.Update)]
    public async Task<IActionResult> SetDefault(int id, CancellationToken ct)
    {
        var result = await _warehouseService.SetDefaultAsync(id, ct);
        return Ok(result);
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
                text = x.Text,

                // Bổ sung để các popup tự chọn kho mặc định
                isDefault = x.IsDefault
            })
        });
    }
}