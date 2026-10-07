using GaoApp.Application.DTOs.Reports;

namespace GaoApp.Application.Interfaces.Services.Reports;

public interface IOperationsReportService
{
    Task<CashFlowReportDto> CashFlowAsync(OperationsReportQueryDto query, CancellationToken ct);
    Task<StockReportDto> StockAsync(OperationsReportQueryDto query, CancellationToken ct);
    Task<DebtReportDto> DebtAsync(OperationsReportQueryDto query, CancellationToken ct);
}
public interface ITreasuryService
{
    Task<TreasurySourcesDto> SourcesAsync(CancellationToken ct, int? expenseId = null, int? payableId = null);
    Task<int> CreateAsync(TreasuryWriteDto request, CancellationToken ct);
    Task<int> ReverseAsync(int id, TreasuryReverseDto request, CancellationToken ct);
    Task OpeningAsync(TreasuryOpeningDto request, CancellationToken ct);
    Task SetDueDateAsync(int id, PayableDueDateDto request, CancellationToken ct);
}
