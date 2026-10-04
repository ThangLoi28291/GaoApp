using GaoApp.Application.Common;
using GaoApp.Application.DTOs.POSShifts;

namespace GaoApp.Application.Interfaces.Services.POSShifts;

public interface IPOSPaymentAdjustmentService
{
    Task<PagedResult<PaymentAdjustmentCandidateDto>> PaymentsAsync(int page, string? keyword, int? paymentId, int? orderId, int? shiftId, CancellationToken ct);
    Task<PagedResult<PaymentAdjustmentItemDto>> ListAsync(int page, string? status, int? shiftId, CancellationToken ct);
    Task<PaymentAdjustmentDetailDto> DetailAsync(int id, CancellationToken ct);
    Task<PaymentAdjustmentPreviewDto> PreviewAsync(int paymentId, GaoApp.Domain.Enums.PaymentMethod method, CancellationToken ct);
    Task<int> CreateAsync(int paymentId, CreatePaymentAdjustmentRequest request, CancellationToken ct);
    Task DecideAsync(int id, string action, PaymentAdjustmentDecision request, CancellationToken ct);
    Task<PagedResult<PaymentAdjustmentCandidateDto>> DepositsAsync(int page, string? keyword, int? entryId, int? shiftId, CancellationToken ct);
    Task<PaymentAdjustmentPreviewDto> PreviewDepositAsync(int entryId, GaoApp.Domain.Enums.PaymentMethod method, CancellationToken ct);
    Task<int> CreateDepositAsync(int entryId, CreatePaymentAdjustmentRequest request, CancellationToken ct);
}
