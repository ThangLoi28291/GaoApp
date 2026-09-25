using GaoApp.Application.DTOs.Reports.Sales;

namespace GaoApp.Application.Interfaces.Services.Reports;

public interface ISalesReportReadService
{
    Task<SalesExecutiveDashboardDto> GetExecutiveDashboardAsync(
        SalesExecutiveDashboardQueryDto request,
        CancellationToken ct = default);

    Task<SalesDetailContextDto> GetDetailContextAsync(
        SalesDetailQueryDto request,
        CancellationToken ct = default);

    Task<SalesPagedResultDto<SalesOrderDetailRowDto>> GetOrdersAsync(
        SalesDetailQueryDto request,
        CancellationToken ct = default);

    Task<SalesPagedResultDto<SalesProductDetailRowDto>> GetProductsAsync(
        SalesDetailQueryDto request,
        CancellationToken ct = default);

    Task<SalesPagedResultDto<SalesDiscountOrderRowDto>> GetDiscountsAsync(
        SalesDetailQueryDto request,
        CancellationToken ct = default);

    Task<SalesPagedResultDto<SalesReturnDetailRowDto>> GetReturnsAsync(
        SalesDetailQueryDto request,
        CancellationToken ct = default);

    Task<SalesPagedResultDto<SalesVoidDetailRowDto>> GetVoidsAsync(
        SalesDetailQueryDto request,
        CancellationToken ct = default);
}
