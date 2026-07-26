using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaoApp.Application.Services.Invoices;

public class ViettelInvoiceListSyncService : IViettelInvoiceListSyncService
{
    private readonly IInvoiceProviderSettingRepository _settingRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IInvoiceIntegrationLogRepository _logRepository;
    private readonly IViettelInvoiceListClient _client;
    private readonly ILegalEntityRepository _legalEntityRepository;
    private readonly ILogger<ViettelInvoiceListSyncService> _logger;

    public ViettelInvoiceListSyncService(
        IInvoiceProviderSettingRepository settingRepository,
        IInvoiceRepository invoiceRepository,
        IInvoiceIntegrationLogRepository logRepository,
        IViettelInvoiceListClient client,
        ILegalEntityRepository legalEntityRepository,
        ILogger<ViettelInvoiceListSyncService>? logger = null)
    {
        _settingRepository = settingRepository;
        _invoiceRepository = invoiceRepository;
        _logRepository = logRepository;
        _client = client;
        _legalEntityRepository = legalEntityRepository;
        _logger = logger ?? NullLogger<ViettelInvoiceListSyncService>.Instance;
    }

    public async Task<Result<ViettelInvoiceListSyncResultDto>> SyncAsync(
        ViettelInvoiceListSyncRequestDto request,
        CancellationToken ct = default)
    {
        var fromDate = request.FromDate.Date;
        var toDate = request.ToDate.Date;

        if (toDate < fromDate)
        {
            return Result<ViettelInvoiceListSyncResultDto>.Failure(
                Error.Validation("Viettel.DateInvalid", "Đến ngày phải lớn hơn hoặc bằng từ ngày."));
        }

        if ((toDate - fromDate).TotalDays > 92)
        {
            return Result<ViettelInvoiceListSyncResultDto>.Failure(
                Error.Validation("Viettel.DateRangeTooLong", "Chỉ đồng bộ tối đa 3 tháng/lần."));
        }

        var legalEntities = await _legalEntityRepository.GetAllAsync(ct);
        var settingIds = legalEntities
            .Where(x => x.IsActive && x.InvoiceProviderSettingId.HasValue)
            .OrderBy(x => x.SalePriority)
            .Select(x => x.InvoiceProviderSettingId!.Value)
            .Distinct()
            .ToList();
        var settings = new List<InvoiceProviderSetting>();

        foreach (var settingId in settingIds)
        {
            var setting = await _settingRepository.GetForInvoiceAsync(
                legalEntities.First(x => x.InvoiceProviderSettingId == settingId).StoreId,
                settingId,
                ct);
            if (setting == null)
            {
                return Result<ViettelInvoiceListSyncResultDto>.Failure(
                    Error.Validation(
                        "InvoiceProvider.NotConfigured",
                        $"Cấu hình Viettel #{settingId} của HKD không còn active."));
            }

            settings.Add(setting);
        }

        // Store legacy chưa có LegalEntity vẫn dùng một setting active như cũ.
        if (settings.Count == 0)
        {
            var legacySetting = await _settingRepository.GetActiveViettelAsync(ct);
            if (legacySetting != null)
                settings.Add(legacySetting);
        }

        if (settings.Count == 0)
        {
            return Result<ViettelInvoiceListSyncResultDto>.Failure(
                Error.Validation("InvoiceProvider.NotConfigured", "Chưa có cấu hình Viettel đang dùng."));
        }

        var result = new ViettelInvoiceListSyncResultDto
        {
            FromDate = fromDate,
            ToDate = toDate
        };

        foreach (var setting in settings)
        {
            var remote = await _client.GetInvoicesAsync(
                baseUrl: setting.BaseUrl,
                username: setting.Username,
                password: setting.Password,
                authMode: setting.AuthMode,
                supplierTaxCode: setting.SupplierTaxCode,
                invoiceType: setting.InvoiceType,
                templateCode: setting.TemplateCode,
                invoiceSeries: setting.InvoiceSeries,
                fromDate: fromDate,
                toDate: toDate,
                pageSize: request.PageSize,
                ct: ct);

            if (!remote.IsSuccess)
                return remote;

            var item = remote.Value;
            result.TotalRemoteRows += item.TotalRemoteRows;
            result.RemoteItemsLoaded += item.RemoteItemsLoaded;
            result.ErrorCount += item.ErrorCount;
            result.DurationMs += item.DurationMs;
            result.RemoteInvoices.AddRange(item.RemoteInvoices);
            result.Messages.AddRange(item.Messages);
            result.Messages.Add(
                $"Đã đồng bộ cấu hình MST {setting.SupplierTaxCode}, ký hiệu {setting.InvoiceSeries}.");
        }

        result.Messages.Add($"Phase 22.7 đã chạy {settings.Count:N0} cấu hình Viettel theo HKD.");

        if (!request.UpdateLocalInvoices)
        {
            result.Messages.Add("Chỉ tải danh sách từ Viettel, chưa cập nhật dữ liệu GaoApp.");
            return Result<ViettelInvoiceListSyncResultDto>.Success(result);
        }

        var remoteItems = result.RemoteInvoices;

        var uuids = remoteItems
            .Select(x => x.TransactionUuid)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var invoiceNos = remoteItems
            .Select(x => x.InvoiceNo)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var localByUuid = await _invoiceRepository.GetByTransactionUuidsAsync(uuids, ct);
        var localByNo = await _invoiceRepository.GetByProviderInvoiceNosAsync(invoiceNos, ct);

        var uuidMap = localByUuid
            .Where(x => !string.IsNullOrWhiteSpace(x.TransactionUuid))
            .GroupBy(x => x.TransactionUuid!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var noMap = localByNo
            .SelectMany(x => new[]
            {
                new { Key = x.ProviderInvoiceNo, Invoice = x },
                new { Key = x.InvoiceNumber, Invoice = x }
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Invoice, StringComparer.OrdinalIgnoreCase);

        int? logInvoiceHeadId = null;

        foreach (var remoteInvoice in remoteItems)
        {
            InvoiceHead? local = null;

            if (!string.IsNullOrWhiteSpace(remoteInvoice.TransactionUuid) &&
                uuidMap.TryGetValue(remoteInvoice.TransactionUuid.Trim(), out var foundByUuid))
            {
                local = foundByUuid;
                result.MatchedByTransactionUuid++;
            }
            else if (!string.IsNullOrWhiteSpace(remoteInvoice.InvoiceNo) &&
                     noMap.TryGetValue(remoteInvoice.InvoiceNo.Trim(), out var foundByNo))
            {
                local = foundByNo;
                result.MatchedByInvoiceNo++;
            }

            if (local == null)
            {
                result.NotMatched++;
                continue;
            }

            logInvoiceHeadId ??= local.Id;

            var changed = SyncLocalInvoice(local, remoteInvoice);

            if (changed)
                result.UpdatedLocalInvoices++;
        }

        await _invoiceRepository.SaveChangesAsync(ct);

        result.Messages.Add($"Đã tải {result.RemoteItemsLoaded:N0}/{result.TotalRemoteRows:N0} hóa đơn từ Viettel.");
        result.Messages.Add($"Khớp UUID: {result.MatchedByTransactionUuid:N0}.");
        result.Messages.Add($"Khớp số hóa đơn: {result.MatchedByInvoiceNo:N0}.");
        result.Messages.Add($"Đã cập nhật local: {result.UpdatedLocalInvoices:N0}.");
        result.Messages.Add($"Không tìm thấy trong GaoApp: {result.NotMatched:N0}.");

        if (logInvoiceHeadId.HasValue)
        {
            await WriteSyncLogAsync(logInvoiceHeadId.Value, result, ct);
        }

        return Result<ViettelInvoiceListSyncResultDto>.Success(result);
    }

    private static bool SyncLocalInvoice(
        InvoiceHead local,
        ViettelInvoiceListItemDto remote)
    {
        var changed = false;

        if (!string.IsNullOrWhiteSpace(remote.InvoiceNo) &&
            !string.Equals(local.ProviderInvoiceNo, remote.InvoiceNo, StringComparison.OrdinalIgnoreCase))
        {
            local.ProviderInvoiceNo = remote.InvoiceNo;
            local.InvoiceNumber = remote.InvoiceNo;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(remote.SupplierTaxCode) &&
            !string.Equals(local.SupplierTaxCode, remote.SupplierTaxCode, StringComparison.OrdinalIgnoreCase))
        {
            local.SupplierTaxCode = remote.SupplierTaxCode;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(remote.TemplateCode) &&
            !string.Equals(local.TemplateCode, remote.TemplateCode, StringComparison.OrdinalIgnoreCase))
        {
            local.TemplateCode = remote.TemplateCode;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(remote.InvoiceSeri) &&
            !string.Equals(local.InvoiceSeries, remote.InvoiceSeri, StringComparison.OrdinalIgnoreCase))
        {
            local.InvoiceSeries = remote.InvoiceSeri;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(remote.TransactionUuid) &&
            string.IsNullOrWhiteSpace(local.TransactionUuid))
        {
            local.TransactionUuid = remote.TransactionUuid;
            changed = true;
        }

        var issuedAtUtc = ParseIssueDateUtc(remote);

        if (issuedAtUtc.HasValue &&
            local.IssuedAtUtc != issuedAtUtc.Value)
        {
            local.IssuedAtUtc = issuedAtUtc.Value;
            changed = true;
        }

        if (remote.State == 1 || remote.StateCode == 1 || !string.IsNullOrWhiteSpace(remote.InvoiceNo))
        {
            if (local.ProviderStatus != InvoiceProviderStatus.Issued &&
                local.ProviderStatus != InvoiceProviderStatus.PdfDownloaded &&
                local.ProviderStatus != InvoiceProviderStatus.ZipDownloaded &&
                local.ProviderStatus != InvoiceProviderStatus.EmailSent)
            {
                local.ProviderStatus = InvoiceProviderStatus.Issued;
                changed = true;
            }

            if (!local.IsLocked)
            {
                local.IsLocked = true;
                local.LockedAtUtc ??= DateTime.UtcNow;
                local.LockReason = string.IsNullOrWhiteSpace(remote.InvoiceNo)
                    ? "Đã đồng bộ phát hành từ Viettel."
                    : $"Đã đồng bộ phát hành từ Viettel. Số hóa đơn: {remote.InvoiceNo}.";
                changed = true;
            }
        }

        local.LastSyncedAtUtc = DateTime.UtcNow;
        local.LastErrorCode = null;
        local.LastErrorMessage = null;

        return changed;
    }

    private async Task WriteSyncLogAsync(
        int invoiceHeadId,
        ViettelInvoiceListSyncResultDto result,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var requestText =
            $"Sync getInvoices {result.FromDate:yyyy-MM-dd} -> {result.ToDate:yyyy-MM-dd}";

        var responseText =
            $"TotalRemoteRows={result.TotalRemoteRows}; Loaded={result.RemoteItemsLoaded}; " +
            $"MatchedUuid={result.MatchedByTransactionUuid}; MatchedNo={result.MatchedByInvoiceNo}; " +
            $"Updated={result.UpdatedLocalInvoices}; NotMatched={result.NotMatched}";

        var log = new InvoiceIntegrationLog
        {
            InvoiceHeadId = invoiceHeadId,
            ActionType = InvoiceIntegrationActionType.SyncInvoiceList,
            RequestUrl = "InvoiceAPI/InvoiceUtilsWS/getInvoices",
            RequestBody = requestText,
            ResponseBody = responseText,
            IsSuccess = true,
            StartedAtUtc = now,
            FinishedAtUtc = now,
            DurationMs = result.DurationMs
        };

        try
        {
            await _logRepository.AddAsync(log, ct);
            await _logRepository.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Local invoice updates have already been persisted.
            _logger.LogWarning(
                "Invoice sync-log persistence failed; synchronized invoice updates remain successful. InvoiceHeadId={InvoiceHeadId}; ExceptionType={ExceptionType}",
                invoiceHeadId,
                ex.GetType().Name);
        }
    }

    private static DateTime? ParseIssueDateUtc(ViettelInvoiceListItemDto item)
    {
        if (item.IssueDate.HasValue && item.IssueDate.Value > 0)
        {
            const long maxUnixTimeMilliseconds = 253402300799999;

            if (item.IssueDate.Value <= maxUnixTimeMilliseconds)
            {
                return DateTimeOffset
                    .FromUnixTimeMilliseconds(item.IssueDate.Value)
                    .UtcDateTime;
            }
        }

        if (!string.IsNullOrWhiteSpace(item.IssueDateStr) &&
            DateTimeOffset.TryParse(
                item.IssueDateStr,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
        {
            return dto.UtcDateTime;
        }

        return null;
    }
}
