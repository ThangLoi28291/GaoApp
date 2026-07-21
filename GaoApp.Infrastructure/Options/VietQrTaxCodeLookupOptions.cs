namespace GaoApp.Infrastructure.Options;

public class VietQrTaxCodeLookupOptions
{
    public const string SectionName = "TaxCodeLookup:VietQr";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "https://api.vietqr.io";

    public int TimeoutSeconds { get; set; } = 8;
}