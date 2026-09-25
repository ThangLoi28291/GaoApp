using GaoApp.Application.DTOs.Reports.Profit;

namespace GaoApp.Application.Interfaces.Services.Reports;

public interface IProfitReportReadService
{
    Task<ProfitReportResponseDto> ReadAsync(ProfitReportQueryDto query, bool activity = false,
        CancellationToken ct = default);
}
