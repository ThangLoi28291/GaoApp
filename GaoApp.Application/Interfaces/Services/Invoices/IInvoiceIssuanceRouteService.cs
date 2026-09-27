using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IInvoiceIssuanceRouteService
{
    /// <summary>
    /// POS dùng đúng một lần sau khi Order đã finalize.
    /// Same-value retry là idempotent.
    /// </summary>
    Task<Result<InvoiceIssuanceRouteDto>> SetInitialRouteAsync(
        int orderId,
        InvoiceIssuanceRoute route,
        CancellationToken ct = default);

    /// <summary>
    /// Manager/Admin đổi Manual <-> Automatic khi invoice còn safe.
    /// </summary>
    Task<Result<InvoiceIssuanceRouteDto>> ChangeRouteAsync(
        int orderId,
        InvoiceIssuanceRoute route,
        CancellationToken ct = default);
}