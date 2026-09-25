namespace GaoApp.Application.Common.Options;

public sealed class InputInvoiceLibraryOptions
{
    public const string SectionName = "InputInvoiceLibrary";

    public bool Enabled { get; set; }
    public string RootPath { get; set; } = string.Empty;
    public int MaxMonthPartitions { get; set; } = 12;
    public int MaxCandidates { get; set; } = 200;
}
