namespace GaoApp.Web.Services.Acb;

public sealed class PendingPaymentQrException(int orderId, int qrId, PosSavedQr? savedQr, string message)
    : InvalidOperationException(message)
{
    public int OrderId { get; } = orderId;
    public int QrId { get; } = qrId;
    public PosSavedQr? SavedQr { get; } = savedQr;
}
