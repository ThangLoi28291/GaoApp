using QRCoder;

namespace GaoApp.Web.Services.Delivery;

public static class DeliveryQrRenderer
{
    public static byte[] Png(string lookupUrl)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(lookupUrl, QRCodeGenerator.ECCLevel.Q);
        using var image = new PngByteQRCode(data);
        return image.GetGraphic(8); // Includes the quiet zone; no System.Drawing dependency.
    }
}
