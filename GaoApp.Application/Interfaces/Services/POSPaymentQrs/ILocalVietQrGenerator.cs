namespace GaoApp.Application.Interfaces.Services.POSPaymentQrs;

public interface ILocalVietQrGenerator
{
    string BuildPayload(
        string bankBin,
        string accountNumber,
        decimal amount,
        string content);

    string GeneratePngDataUrl(string payload);
}