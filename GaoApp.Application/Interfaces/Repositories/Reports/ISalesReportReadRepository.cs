using GaoApp.Application.DTOs.Reports.Sales;

namespace GaoApp.Application.Interfaces.Repositories.Reports;

public interface ISalesReportReadRepository
{
    Task<SalesExecutivePeriodDataDto> GetExecutivePeriodAsync(
        int storeId,
        SalesResolvedExecutiveQueryDto query,
        CancellationToken ct = default);

    Task<List<SalesTerminalOptionDto>> GetTerminalOptionsAsync(
        int storeId,
        CancellationToken ct = default);

    Task<SalesPagedResultDto<SalesOrderDetailRowDto>> GetOrdersAsync(
        int storeId,
        SalesResolvedDetailQueryDto query,
        CancellationToken ct = default);

    Task<SalesPagedResultDto<SalesProductDetailRowDto>> GetProductsAsync(
        int storeId,
        SalesResolvedDetailQueryDto query,
        CancellationToken ct = default);

    Task<SalesPagedResultDto<SalesDiscountOrderRowDto>> GetDiscountsAsync(
        int storeId,
        SalesResolvedDetailQueryDto query,
        CancellationToken ct = default);

    Task<SalesPagedResultDto<SalesReturnDetailRowDto>> GetReturnsAsync(
        int storeId,
        SalesResolvedDetailQueryDto query,
        CancellationToken ct = default);

    Task<SalesPagedResultDto<SalesVoidDetailRowDto>> GetVoidsAsync(
        int storeId,
        SalesResolvedDetailQueryDto query,
        CancellationToken ct = default);
}
