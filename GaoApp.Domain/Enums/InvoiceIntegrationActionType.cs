namespace GaoApp.Domain.Enums;

/// <summary>
/// Loại thao tác tích hợp hóa đơn điện tử.
/// Dùng để ghi log request/response khi gọi API Viettel.
/// </summary>
public enum InvoiceIntegrationActionType
{
    Login = 1,

    BuildPayload = 2,

    PreviewDraft = 3,

    CreateDraft = 4,

    IssueInvoice = 5,

    SearchByTransactionUuid = 6,

    DownloadPdf = 7,

    DownloadZip = 8,

    SendEmail = 9,

    UpdatePaymentStatus = 10,

    CancelPaymentStatus = 11,

    UpdatePrintStatus = 12,

    CancelInvoice = 13,
    SyncInvoiceList = 14,
    IssueReplacementInvoice = 15,
    IssueAdjustmentInvoice = 16
}