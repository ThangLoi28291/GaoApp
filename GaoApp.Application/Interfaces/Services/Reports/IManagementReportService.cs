using GaoApp.Application.DTOs.Reports;

namespace GaoApp.Application.Interfaces.Services.Reports;

public interface IManagementReportService
{
    Task<ManagementReportDto> ReadAsync(ManagementReportQueryDto query, CancellationToken ct = default);
}
public interface IOperatingExpenseService
{
    Task<ExpenseListDto> ListAsync(DateTime from, DateTime to, string? search, string? status, int page, CancellationToken ct);
    Task<ExpenseRowDto> CreateAsync(ExpenseWriteDto request, CancellationToken ct);
    Task<ExpenseRowDto> UpdateAsync(int id, ExpenseWriteDto request, CancellationToken ct);
    Task<ExpenseRowDto> TransitionAsync(int id, string action, ExpenseActionDto request, CancellationToken ct);
}
