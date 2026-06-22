using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IInvoiceViettelDashboardService
{
    Task<Result<ViettelInvoiceDashboardDto>> GetDashboardAsync(
        ViettelInvoiceDashboardQueryDto query,
        CancellationToken ct = default);
}