using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;

namespace GaoApp.Application.Services.Invoices;

public class InvoiceIntegrationLogCleanupService : IInvoiceIntegrationLogCleanupService
{
    private readonly IInvoiceIntegrationLogRepository _logRepository;

    public InvoiceIntegrationLogCleanupService(
        IInvoiceIntegrationLogRepository logRepository)
    {
        _logRepository = logRepository;
    }

    public async Task<Result<InvoiceIntegrationLogCleanupResultDto>> CleanupAsync(
        InvoiceIntegrationLogCleanupRequestDto request,
        CancellationToken ct = default)
    {
        if (request.MaxRowsPerRun <= 0)
        {
            return Result<InvoiceIntegrationLogCleanupResultDto>.Failure(
                Error.Validation(
                    "InvoiceIntegrationLogCleanup.MaxRowsInvalid",
                    "Số dòng tối đa mỗi lần dọn phải lớn hơn 0."));
        }

        if (request.MaxRowsPerRun > 50000)
        {
            return Result<InvoiceIntegrationLogCleanupResultDto>.Failure(
                Error.Validation(
                    "InvoiceIntegrationLogCleanup.MaxRowsTooLarge",
                    "Không nên dọn quá 50.000 log trong một lần."));
        }

        if (request.PreviewDraftSuccessDays < 1 ||
            request.BuildPayloadSuccessDays < 1 ||
            request.SendEmailSuccessDays < 1 ||
            request.SyncInvoiceListSuccessDays < 1 ||
            request.DownloadFileSuccessDays < 1 ||
            request.OtherSuccessDays < 1 ||
            request.FailedLogDays < 1)
        {
            return Result<InvoiceIntegrationLogCleanupResultDto>.Failure(
                Error.Validation(
                    "InvoiceIntegrationLogCleanup.RetentionDaysInvalid",
                    "Số ngày lưu log phải lớn hơn hoặc bằng 1."));
        }

        var nowUtc = DateTime.UtcNow;

        var result = await _logRepository.CleanupAsync(
            request,
            nowUtc,
            ct);

        return Result<InvoiceIntegrationLogCleanupResultDto>.Success(result);
    }
}