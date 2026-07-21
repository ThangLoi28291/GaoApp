namespace GaoApp.Domain.Enums;

public enum InvoiceCorrectionStatus
{
    Draft = 0,

    ReadyToIssue = 1,

    Issuing = 2,

    Issued = 3,

    Failed = 4,

    Cancelled = 5
}