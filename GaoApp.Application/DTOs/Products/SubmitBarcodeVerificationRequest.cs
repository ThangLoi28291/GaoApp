namespace GaoApp.Application.DTOs.Products.BarcodeVerification;

public class SubmitBarcodeVerificationRequest
{
    public List<SubmitBarcodeVerificationItemRequest> Items { get; set; } = new();
}

public class SubmitBarcodeVerificationItemRequest
{
    public int ProductUnitConversionId { get; set; }

    public string? SuggestedBarcode { get; set; }

    public bool NoSupplierBarcode { get; set; }

    public string? Note { get; set; }
}