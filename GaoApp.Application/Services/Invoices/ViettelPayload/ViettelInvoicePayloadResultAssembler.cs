using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Invoices;

internal static class ViettelInvoicePayloadResultAssembler
{
    public static ViettelInvoicePayloadResultDto Build(
        InvoiceHeadDto invoice,
        ViettelSettingSnapshot snapshot,
        string transactionUuid,
        ViettelSummaryBuildResult summaryResult,
        List<string> warnings,
        ViettelInvoicePayloadDto payload,
        string json,
        IEnumerable<InvoiceIntegrationLog> latestLogs,
        ViettelInvoiceUiState uiState)
    {
        return new ViettelInvoicePayloadResultDto
        {
            InvoiceHeadId = invoice.Id,
            StoreId = invoice.StoreId,
            OrderId = invoice.OrderId,
            OrderNumber = invoice.InvoiceNumber,

            SupplierTaxCode = snapshot.SupplierTaxCode,
            TemplateCode = snapshot.TemplateCode,
            InvoiceSeries = snapshot.InvoiceSeries,
            TransactionUuid = transactionUuid,

            TotalAmountWithoutTax = summaryResult.TotalAmountWithoutTax,
            TotalTaxAmount = summaryResult.TotalTaxAmount,
            TotalAmountWithTax = summaryResult.TotalAmountWithTax,

            Warnings = warnings ?? new List<string>(),
            Payload = payload,
            Json = json ?? string.Empty,

            ProviderStatus = uiState.ProviderStatus,
            ProviderStatusName = uiState.ProviderStatusName,
            ProviderStatusBadgeClass = uiState.ProviderStatusBadgeClass,

            ProviderInvoiceNo = invoice.ProviderInvoiceNo,
            LastErrorCode = invoice.LastErrorCode,
            LastErrorMessage = invoice.LastErrorMessage,
            IssuedAtUtc = invoice.IssuedAtUtc,
            LastSyncedAtUtc = invoice.LastSyncedAtUtc,
            PdfFilePath = invoice.PdfFilePath,
            ZipFilePath = invoice.ZipFilePath,
            OfficialPdfStatus = invoice.OfficialPdfStatus,
            OfficialPdfDownloadedAtUtc = invoice.OfficialPdfDownloadedAtUtc,
            OfficialPdfFileName = invoice.OfficialPdfFileName,

            OfficialZipXmlStatus = invoice.OfficialZipXmlStatus,
            OfficialZipXmlDownloadedAtUtc = invoice.OfficialZipXmlDownloadedAtUtc,
            OfficialZipXmlFileName = invoice.OfficialZipXmlFileName,

            EmailStatus = invoice.EmailStatus,
            EmailSentAtUtc = invoice.EmailSentAtUtc,
            LastEmailTo = invoice.LastEmailTo,
            EmailSendCount = invoice.EmailSendCount,
            LastEmailErrorMessage = invoice.LastEmailErrorMessage,

            CanSendEmail = uiState.CanSendEmail,
            IsIssued = uiState.IsIssued,
            HasOfficialPdf = uiState.HasOfficialPdf,
            HasOfficialZip = uiState.HasOfficialZip,

            CanPreviewDraft = uiState.CanPreviewDraft,
            CanIssue = uiState.CanIssue,
            CanSyncByUuid = uiState.CanSyncByUuid,
            CanDownloadOfficialFiles = uiState.CanDownloadOfficialFiles,
            CanViewSavedPdf = uiState.CanViewSavedPdf,
            CanDownloadSavedZip = uiState.CanDownloadSavedZip,
            RequiresUuidSyncBeforeIssue = uiState.RequiresUuidSyncBeforeIssue,
            IssueBlockReason = uiState.IssueBlockReason,

            Logs = ViettelInvoiceLogMapper.Map(latestLogs)
        };
    }
}