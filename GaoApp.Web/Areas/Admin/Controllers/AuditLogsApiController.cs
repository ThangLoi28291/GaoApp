using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionCodes.System.AuditLog.View)]
[ApiController]
[Route("admin/api/audit-logs")]
public class AuditLogsApiController : ControllerBase
{
    private readonly IAuditLogService _auditLogService;

    public AuditLogsApiController(IAuditLogService auditLogService)
    {
        _auditLogService = auditLogService;
    }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] AuditLogQueryDto query, CancellationToken ct)
    {
        var result = await _auditLogService.SearchAsync(query, ct);
        return Ok(result);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetDetail(long id, CancellationToken ct)
    {
        var result = await _auditLogService.GetDetailAsync(id, ct);
        if (result == null)
            return NotFound();

        return Ok(result);
    }
}
