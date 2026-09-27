using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IInvoiceBuyerSelfServiceService
{
    Task<Result<InvoiceBuyerSelfServiceLinkDto>> CreateLinkAsync(
        int orderId,
        CancellationToken ct = default);

    Task<Result<InvoiceBuyerSelfServiceViewDto>> GetAsync(
        string token,
        CancellationToken ct = default);

    Task<Result<InvoiceBuyerSelfServiceViewDto>> SubmitAsync(
        string token,
        SubmitInvoiceBuyerSelfServiceRequest request,
        CancellationToken ct = default);
}