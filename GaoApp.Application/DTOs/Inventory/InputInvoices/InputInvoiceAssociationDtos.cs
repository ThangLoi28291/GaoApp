namespace GaoApp.Application.DTOs.Inventory.InputInvoices;

public static class InputInvoiceAssociationLifecycleStates
{
    public const string WaitingXml = "WaitingXml";
    public const string Linked = "Linked";
    public const string Unlinked = "Unlinked";
}

public static class InputInvoiceAssociationMutationOutcomes
{
    public const string Applied = "Applied";
    public const string AlreadyApplied = "AlreadyApplied";
}

public sealed class InputInvoiceAssociationCapabilitiesDto
{
    public bool CanLink { get; set; }
    public bool CanUnlink { get; set; }
    public bool CanRelink { get; set; }
    public bool InitialLinkRequiresConfirmation { get; set; } = true;
    public bool UnlinkReasonRequired { get; set; } = true;
    public bool RelinkReasonRequired { get; set; } = true;
    public string? BlockReasonCode { get; set; }
    public string? BlockMessage { get; set; }
}

public sealed class InputInvoiceAssociationContextDto
{
    public int StockDocumentId { get; set; }
    public string ReceiptStatus { get; set; } = string.Empty;
    public bool IsConfirmed { get; set; }
    public string LifecycleState { get; set; } = InputInvoiceAssociationLifecycleStates.Unlinked;
    public bool IsWaitingXml => string.Equals(
        LifecycleState,
        InputInvoiceAssociationLifecycleStates.WaitingXml,
        StringComparison.Ordinal);
    public int? CurrentInputInvoiceHeadId { get; set; }
    public string? CurrentInvoiceSeries { get; set; }
    public string? CurrentInvoiceNumber { get; set; }
    public string? ReconciliationState { get; set; }
    public bool IsLateAssociationException { get; set; }
    public InputInvoiceAssociationCapabilitiesDto Capabilities { get; set; } = new();
}

public sealed class UnlinkInputInvoiceRequest
{
    public int ExpectedCurrentInputInvoiceHeadId { get; set; }
    public string? Reason { get; set; }
}

public sealed class RelinkInputInvoiceRequest
{
    public string DocumentKey { get; set; } = string.Empty;
    public int ExpectedCurrentInputInvoiceHeadId { get; set; }
    public string? Reason { get; set; }
}

public sealed class InputInvoiceAssociationMutationResultDto
{
    public string Outcome { get; set; } = InputInvoiceAssociationMutationOutcomes.Applied;
    public InputInvoiceAssociationContextDto Association { get; set; } = new();
}
