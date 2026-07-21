using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

internal static class ViettelInvoiceLogMapper
{
    public static List<InvoiceIntegrationLogItemDto> Map(
        IEnumerable<InvoiceIntegrationLog> logs)
    {
        return logs
            .Select(x => new InvoiceIntegrationLogItemDto
            {
                Id = x.Id,
                ActionName = GetActionName(x.ActionType),
                IsSuccess = x.IsSuccess,
                ErrorCode = x.ErrorCode,
                ErrorMessage = x.ErrorMessage,
                RequestUrl = x.RequestUrl,
                StartedAtUtc = x.StartedAtUtc,
                FinishedAtUtc = x.FinishedAtUtc,
                DurationMs = x.DurationMs
            })
            .ToList();
    }

    private static string GetActionName(InvoiceIntegrationActionType actionType)
    {
        return actionType switch
        {
            InvoiceIntegrationActionType.PreviewDraft => "Preview PDF nháp",
            InvoiceIntegrationActionType.IssueInvoice => "Phát hành Viettel",
            InvoiceIntegrationActionType.SearchByTransactionUuid => "Tra cứu UUID",
            InvoiceIntegrationActionType.DownloadPdf => "Tải PDF chính thức",
            InvoiceIntegrationActionType.DownloadZip => "Tải ZIP/XML",
            InvoiceIntegrationActionType.SendEmail => "Gửi email",
            InvoiceIntegrationActionType.SyncInvoiceList => "Đồng bộ danh sách",
            InvoiceIntegrationActionType.IssueReplacementInvoice => "Phát hành hóa đơn thay thế",
            InvoiceIntegrationActionType.IssueAdjustmentInvoice => "Phát hành hóa đơn điều chỉnh",
            _ => actionType.ToString()
        };
    }
}