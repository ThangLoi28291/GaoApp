using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IViettelInvoiceIssueService
{
    Task<Result<ViettelInvoiceIssueResultDto>> IssueAsync(
        int invoiceHeadId,
        CancellationToken ct = default);
}