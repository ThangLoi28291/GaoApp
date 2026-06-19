using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public class InvoiceIntegrationLogService : IInvoiceIntegrationLogService
{
    private readonly IInvoiceIntegrationLogRepository _logRepository;

    public InvoiceIntegrationLogService(
        IInvoiceIntegrationLogRepository logRepository)
    {
        _logRepository = logRepository;
    }

    public async Task<Result<PagedResult<InvoiceIntegrationLogListItemDto>>> GetLogsAsync(
        InvoiceIntegrationLogQueryDto query,
        CancellationToken ct = default)
    {
        query.Page = query.Page <= 0 ? 1 : query.Page;
        query.PageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        var (items, total) = await _logRepository.QueryAsync(query, ct);

        var dtoItems = items
            .Select(MapListItem)
            .ToList();

        return Result<PagedResult<InvoiceIntegrationLogListItemDto>>.Success(
            new PagedResult<InvoiceIntegrationLogListItemDto>
            {
                Items = dtoItems,
                Page = query.Page,
                PageSize = query.PageSize,
                TotalItems = total
            });
    }

    public async Task<Result<InvoiceIntegrationLogDetailDto>> GetDetailAsync(
        int id,
        CancellationToken ct = default)
    {
        if (id <= 0)
        {
            return Result<InvoiceIntegrationLogDetailDto>.Failure(
                Error.Validation(
                    "InvoiceIntegrationLog.InvalidId",
                    "LogId không hợp lệ."));
        }

        var entity = await _logRepository.GetByIdAsync(id, ct);

        if (entity == null)
        {
            return Result<InvoiceIntegrationLogDetailDto>.Failure(
                Error.NotFound("Không tìm thấy log tích hợp."));
        }

        return Result<InvoiceIntegrationLogDetailDto>.Success(
            MapDetail(entity));
    }

    private static InvoiceIntegrationLogListItemDto MapListItem(
        InvoiceIntegrationLog entity)
    {
        return new InvoiceIntegrationLogListItemDto
        {
            Id = entity.Id,
            InvoiceHeadId = entity.InvoiceHeadId,
            OrderId = entity.InvoiceHead?.OrderId,
            InvoiceNumber = entity.InvoiceHead?.InvoiceNumber,
            ProviderInvoiceNo = entity.InvoiceHead?.ProviderInvoiceNo,
            ActionType = entity.ActionType,
            ActionName = GetActionName(entity.ActionType),
            IsSuccess = entity.IsSuccess,
            ErrorCode = entity.ErrorCode,
            ErrorMessage = entity.ErrorMessage,
            RequestUrl = entity.RequestUrl,
            StartedAtUtc = entity.StartedAtUtc,
            FinishedAtUtc = entity.FinishedAtUtc,
            DurationMs = entity.DurationMs
        };
    }

    private static InvoiceIntegrationLogDetailDto MapDetail(
        InvoiceIntegrationLog entity)
    {
        return new InvoiceIntegrationLogDetailDto
        {
            Id = entity.Id,
            InvoiceHeadId = entity.InvoiceHeadId,
            OrderId = entity.InvoiceHead?.OrderId,
            InvoiceNumber = entity.InvoiceHead?.InvoiceNumber,
            ProviderInvoiceNo = entity.InvoiceHead?.ProviderInvoiceNo,
            ActionType = entity.ActionType,
            ActionName = GetActionName(entity.ActionType),
            IsSuccess = entity.IsSuccess,
            ErrorCode = entity.ErrorCode,
            ErrorMessage = entity.ErrorMessage,
            RequestUrl = entity.RequestUrl,
            RequestBody = entity.RequestBody,
            ResponseBody = entity.ResponseBody,
            StartedAtUtc = entity.StartedAtUtc,
            FinishedAtUtc = entity.FinishedAtUtc,
            DurationMs = entity.DurationMs
        };
    }

    private static string GetActionName(InvoiceIntegrationActionType actionType)
    {
        return actionType switch
        {
            InvoiceIntegrationActionType.Login => "Đăng nhập",
            InvoiceIntegrationActionType.BuildPayload => "Build JSON",
            InvoiceIntegrationActionType.PreviewDraft => "Preview PDF nháp",
            InvoiceIntegrationActionType.CreateDraft => "Tạo nháp",
            InvoiceIntegrationActionType.IssueInvoice => "Phát hành Viettel",
            InvoiceIntegrationActionType.SearchByTransactionUuid => "Tra cứu UUID",
            InvoiceIntegrationActionType.DownloadPdf => "Tải PDF chính thức",
            InvoiceIntegrationActionType.DownloadZip => "Tải ZIP/XML",
            InvoiceIntegrationActionType.SendEmail => "Gửi email",
            InvoiceIntegrationActionType.UpdatePaymentStatus => "Cập nhật thanh toán",
            InvoiceIntegrationActionType.CancelPaymentStatus => "Hủy thanh toán",
            InvoiceIntegrationActionType.UpdatePrintStatus => "Cập nhật trạng thái in",
            InvoiceIntegrationActionType.CancelInvoice => "Hủy hóa đơn",
            InvoiceIntegrationActionType.SyncInvoiceList => "Đồng bộ danh sách",
            InvoiceIntegrationActionType.IssueReplacementInvoice => "Phát hành hóa đơn thay thế",
            InvoiceIntegrationActionType.IssueAdjustmentInvoice => "Phát hành hóa đơn điều chỉnh",
            _ => actionType.ToString()
        };
    }
}