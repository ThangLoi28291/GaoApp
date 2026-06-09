using GaoApp.Application.DTOs.POS;
using GaoApp.Application.DTOs.POSPaymentQrs;

namespace GaoApp.Application.Interfaces.Services.POSPaymentQrs;

public interface IPOSPaymentQrService
{
    Task<POSPaymentQrDto> CreateLocalManualQrAsync(
        OrderDraftDto currentDraft,
        CreatePOSPaymentQrRequest request,
        CancellationToken ct = default);
    Task MarkManualConfirmedAsync(
    int qrRequestId,
    int? userId,
    CancellationToken ct = default);

    Task CancelAsync(
        int qrRequestId,
        CancellationToken ct = default);
    Task CancelByContentAsync(
    string content,
    CancellationToken ct = default);
}