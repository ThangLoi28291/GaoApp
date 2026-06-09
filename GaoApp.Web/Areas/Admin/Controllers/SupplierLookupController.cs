using GaoApp.Application.Common;
using GaoApp.Application.Interfaces.Services.Suppliers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/api/suppliers")]
[Authorize]
[ApiController]
public class SupplierLookupController : ControllerBase
{
    private readonly ISupplierService _supplierService;
    private readonly ITenantContext _tenantContext;

    public SupplierLookupController(
        ISupplierService supplierService,
        ITenantContext tenantContext)
    {
        _supplierService = supplierService;
        _tenantContext = tenantContext;
    }

    [HttpGet("select2")]
    public async Task<IActionResult> Select2([FromQuery] string? term, CancellationToken ct)
    {
        try
        {
            if (!_tenantContext.StoreId.HasValue || _tenantContext.StoreId.Value <= 0)
            {
                return StatusCode(500, new
                {
                    message = "Không lấy được StoreId hiện tại từ TenantContext."
                });
            }

            var items = await _supplierService.SearchSelect2Async(
                _tenantContext.StoreId.Value,
                term,
                ct);

            return Ok(new
            {
                results = items.Select(x => new
                {
                    id = x.Id,
                    text = x.Text
                })
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = ex.Message,
                detail = ex.InnerException?.Message
            });
        }
    }
}