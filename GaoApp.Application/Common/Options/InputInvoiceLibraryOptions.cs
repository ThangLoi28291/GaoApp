namespace GaoApp.Application.Common.Options;

public sealed class InputInvoiceLibraryOptions
{
    public const string SectionName = "InputInvoiceLibrary";

    public bool Enabled { get; set; }
    // "XML" (or empty) uses the XML folder under Storage:UploadRoot.
    // An absolute path remains supported for an existing external library.
    public string RootPath { get; set; } = "XML";
    public bool CreateIfMissing { get; set; }
    public int MaxMonthPartitions { get; set; } = 12;
    public int MaxCandidates { get; set; } = 200;
}
