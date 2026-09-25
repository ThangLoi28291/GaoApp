using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IViettelInvoicePayloadBuilder
{
    Task<Result<ViettelInvoicePayloadResultDto>> BuildAsync(
        int invoiceHeadId,
        CancellationToken ct = default);

    Task<Result<ViettelInvoicePayloadResultDto>> BuildForIssueAsync(
        int invoiceHeadId,
        InvoiceProviderSetting setting,
        string transactionUuid,
        CancellationToken ct = default);
}
