using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Interfaces.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/reports/sales")]
[Authorize(Policy = PermissionCodes.Report.Sales.View)]
public sealed class SalesReportController : BaseAdminController
{
    private readonly ISalesReportReadService _readService;

    public SalesReportController(ISalesReportReadService readService)
    {
        _readService = readService;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Báo cáo bán hàng";
        return View();
    }

    [HttpGet("context")]
    public Task<SalesDetailContextDto> Context([FromQuery] SalesDetailQueryDto query, CancellationToken ct)
        => _readService.GetDetailContextAsync(query, ct);

    [HttpGet("orders")]
    public Task<SalesPagedResultDto<SalesOrderDetailRowDto>> Orders([FromQuery] SalesDetailQueryDto query, CancellationToken ct)
        => _readService.GetOrdersAsync(query, ct);

    [HttpGet("products")]
    public Task<SalesPagedResultDto<SalesProductDetailRowDto>> Products([FromQuery] SalesDetailQueryDto query, CancellationToken ct)
        => _readService.GetProductsAsync(query, ct);

    [HttpGet("discounts")]
    public Task<SalesPagedResultDto<SalesDiscountOrderRowDto>> Discounts([FromQuery] SalesDetailQueryDto query, CancellationToken ct)
        => _readService.GetDiscountsAsync(query, ct);

    [HttpGet("returns")]
    public Task<SalesPagedResultDto<SalesReturnDetailRowDto>> Returns([FromQuery] SalesDetailQueryDto query, CancellationToken ct)
        => _readService.GetReturnsAsync(query, ct);

    [HttpGet("voids")]
    public Task<SalesPagedResultDto<SalesVoidDetailRowDto>> Voids([FromQuery] SalesDetailQueryDto query, CancellationToken ct)
        => _readService.GetVoidsAsync(query, ct);
}
