using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.DTOs.Reports.Sales;

namespace GaoApp.Application.Interfaces.Repositories.Reports;

public interface IProfitReportReadRepository
{
    Task<ProfitSourceSnapshot> ReadAsync(int storeId, SalesResolvedPeriodSet periods,
        bool activity, CancellationToken ct = default);
}
