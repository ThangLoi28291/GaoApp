using GaoApp.Application.Interfaces.Services.POSPaymentQrs;
using QRCoder;

namespace GaoApp.Infrastructure.Services.POSPaymentQrs;

public class LocalVietQrGenerator : ILocalVietQrGenerator
{
    public string BuildPayload(
        string bankBin,
        string accountNumber,
        decimal amount,
        string content)
    {
        bankBin = Clean(bankBin);
        accountNumber = Clean(accountNumber);
        content = (content ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(bankBin))
            throw new InvalidOperationException("Ngân hàng chưa có BIN VietQR.");

        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new InvalidOperationException("Ngân hàng chưa có số tài khoản.");

        var amountText = decimal.Truncate(amount).ToString("0");

        var consumerAccount =
            Tlv("00", bankBin) +
            Tlv("01", accountNumber);

        var merchantAccount =
            Tlv("00", "A000000727") +
            Tlv("01", consumerAccount) +
            Tlv("02", "QRIBFTTA");

        var payloadWithoutCrc =
            Tlv("00", "01") +                 // Payload Format Indicator
            Tlv("01", "12") +                 // Dynamic QR
            Tlv("38", merchantAccount) +      // VietQR merchant account
            Tlv("53", "704") +                // VND
            Tlv("54", amountText) +           // Amount
            Tlv("58", "VN") +                 // Country
            Tlv("62", Tlv("08", content)) +   // Purpose/content
            "6304";                           // CRC placeholder

        var crc = Crc16Ccitt(payloadWithoutCrc);

        return payloadWithoutCrc + crc;
    }

    public string GeneratePngDataUrl(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);

        var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(12);

        return "data:image/png;base64," + Convert.ToBase64String(bytes);
    }

    private static string Clean(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string Tlv(string id, string value)
    {
        value ??= string.Empty;
        return id + value.Length.ToString("00") + value;
    }

    private static string Crc16Ccitt(string input)
    {
        ushort crc = 0xFFFF;

        foreach (var c in input)
        {
            crc ^= (ushort)(c << 8);

            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x1021)
                    : (ushort)(crc << 1);
            }
        }

        return crc.ToString("X4");
    }
}