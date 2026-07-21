namespace GaoApp.Infrastructure.Services.Invoices;

public class TaxCodeLookupOptions
{
    public bool Enabled { get; set; } = true;

    public string Provider { get; set; } = "VietQR";

    public string BaseUrl { get; set; } = "https://api.vietqr.io";

    public int TimeoutSeconds { get; set; } = 8;
}