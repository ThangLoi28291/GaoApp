using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IInvoiceDashboardService
{
    Task<Result<InvoiceDashboardDto>> GetDashboardAsync(
        InvoiceDashboardQueryDto query,
        CancellationToken ct = default);
}