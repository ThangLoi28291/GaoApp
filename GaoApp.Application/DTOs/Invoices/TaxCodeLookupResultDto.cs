namespace GaoApp.Application.DTOs.Invoices;

public class TaxCodeLookupResultDto
{
    public bool IsFound { get; set; }

    public string? TaxCode { get; set; }

    public string? CompanyName { get; set; }

    public string? InternationalName { get; set; }

    public string? ShortName { get; set; }

    public string? Address { get; set; }

    public string? LegalRepresentative { get; set; }

    public string Source { get; set; } = "vietqr";

    public string? RawCode { get; set; }

    public string? RawMessage { get; set; }
}