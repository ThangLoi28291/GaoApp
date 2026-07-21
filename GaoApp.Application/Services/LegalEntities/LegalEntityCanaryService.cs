using System.Text.Json;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.LegalEntities;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Application.Interfaces.Services.LegalEntities;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.LegalEntities;

public sealed class LegalEntityCanaryService : ILegalEntityCanaryService
{
    private readonly ILegalEntityService _legalEntityService;
    private readonly ILegalEntityCanaryRepository _canaryRepository;
    private readonly ICurrentStore _currentStore;
    private readonly ICurrentUser _currentUser;

    public LegalEntityCanaryService(
        ILegalEntityService legalEntityService,
        ILegalEntityCanaryRepository canaryRepository,
        ICurrentStore currentStore,
        ICurrentUser currentUser)
    {
        _legalEntityService = legalEntityService;
        _canaryRepository = canaryRepository;
        _currentStore = currentStore;
        _currentUser = currentUser;
    }

    public async Task<Result<LegalEntityCanaryStatusDto>> GetStatusAsync(
        CancellationToken ct = default)
    {
        var store = await _canaryRepository.GetStoreAsync(
            _currentStore.StoreId,
            forUpdate: false,
            ct);
        if (store == null)
            return Result<LegalEntityCanaryStatusDto>.Failure(Error.NotFound("Không tìm thấy cửa hàng hiện tại."));

        var preflightResult = await _legalEntityService.GetActivationPreflightAsync(ct);
        if (!preflightResult.IsSuccess)
            return Result<LegalEntityCanaryStatusDto>.Failure(preflightResult.Error);

        var preflight = preflightResult.Value;
        var events = await _canaryRepository.GetRecentEventsAsync(store.Id, 10, ct);
        var monitoringSince = ResolveMonitoringSince(store, events);
        var now = DateTime.UtcNow;
        var metrics = new LegalEntityCanaryOperationalMetricsDto();

        if (monitoringSince.HasValue)
        {
            metrics = await _canaryRepository.GetOperationalMetricsAsync(
                store.Id,
                monitoringSince.Value,
                now,
                ct);

            var mismatchCounts = await _canaryRepository.GetReconciliationMismatchCountsAsync(
                store.Id,
                monitoringSince.Value,
                now,
                ct);
            metrics.AllocationMismatchCount = mismatchCounts.AllocationMismatchCount;
            metrics.InvoiceMismatchCount = mismatchCounts.InvoiceMismatchCount;
        }

        var checks = BuildHealthChecks(store.IsMultiLegalEntityEnabled, preflight, metrics);
        var healthStatus = ResolveHealthStatus(store.IsMultiLegalEntityEnabled, checks);

        return Result<LegalEntityCanaryStatusDto>.Success(new LegalEntityCanaryStatusDto
        {
            IsEnabled = store.IsMultiLegalEntityEnabled,
            ActivatedAtUtc = store.MultiLegalEntityActivatedAtUtc,
            HealthStatus = healthStatus,
            HealthMessage = BuildHealthMessage(healthStatus, metrics),
            IsConfigurationReady = preflight.IsConfigurationReady,
            CanActivate = preflight.CanActivate,
            EvaluatedAtUtc = now,
            MonitoringSinceUtc = monitoringSince,
            Metrics = metrics,
            HealthChecks = checks,
            RecentEvents = events.Select(MapEvent).ToList()
        });
    }

    public async Task<Result<LegalEntityCanaryStateChangeDto>> SetStateAsync(
        SetMultiLegalEntityEnabledRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? string.Empty
            : request.Reason.Trim();
        if (reason.Length < 5)
        {
            return Result<LegalEntityCanaryStateChangeDto>.Failure(Error.Validation(
                "LegalEntity.ActivationReasonRequired",
                "Vui lòng nhập lý do bật/tắt ít nhất 5 ký tự để lưu nhật ký vận hành."));
        }
        if (reason.Length > 500)
        {
            return Result<LegalEntityCanaryStateChangeDto>.Failure(Error.Validation(
                "LegalEntity.ActivationReasonTooLong",
                "Lý do bật/tắt không được vượt quá 500 ký tự."));
        }

        LegalEntityActivationPreflightDto? preflight = null;
        if (request.IsEnabled)
        {
            var preflightResult = await _legalEntityService.GetActivationPreflightAsync(ct);
            if (!preflightResult.IsSuccess)
                return Result<LegalEntityCanaryStateChangeDto>.Failure(preflightResult.Error);

            preflight = preflightResult.Value;
            if (!preflight.IsConfigurationReady)
            {
                var blockers = preflight.Checks
                    .Where(x => !x.IsPassed && string.Equals(x.Level, "Error", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Title)
                    .Take(3)
                    .ToList();
                return Result<LegalEntityCanaryStateChangeDto>.Failure(Error.Validation(
                    "LegalEntity.ActivationPreflightFailed",
                    $"Chưa thể bật Multi LegalEntity. Cần xử lý: {string.Join("; ", blockers)}."));
            }
        }

        var store = await _canaryRepository.GetStoreAsync(
            _currentStore.StoreId,
            forUpdate: true,
            ct);
        if (store == null)
            return Result<LegalEntityCanaryStateChangeDto>.Failure(Error.NotFound("Không tìm thấy cửa hàng hiện tại."));

        if (request.ExpectedCurrentState.HasValue &&
            request.ExpectedCurrentState.Value != store.IsMultiLegalEntityEnabled)
        {
            return Result<LegalEntityCanaryStateChangeDto>.Failure(Error.Conflict(
                "Trạng thái Multi LegalEntity đã được người khác thay đổi. Hãy tải lại trước khi thao tác."));
        }

        if (store.IsMultiLegalEntityEnabled == request.IsEnabled)
        {
            return Result<LegalEntityCanaryStateChangeDto>.Success(new LegalEntityCanaryStateChangeDto
            {
                IsEnabled = store.IsMultiLegalEntityEnabled,
                WasChanged = false,
                ActivatedAtUtc = store.MultiLegalEntityActivatedAtUtc,
                Message = store.IsMultiLegalEntityEnabled
                    ? "Multi LegalEntity đã ở trạng thái bật."
                    : "Kill switch đã ở trạng thái tắt."
            });
        }

        var now = DateTime.UtcNow;
        var previousState = store.IsMultiLegalEntityEnabled;
        var previousActivationAt = store.MultiLegalEntityActivatedAtUtc;
        var recentEvents = await _canaryRepository.GetRecentEventsAsync(store.Id, 50, ct);
        var action = request.IsEnabled
            ? recentEvents.Count > 0
                ? LegalEntityActivationAction.Reactivate
                : LegalEntityActivationAction.Activate
            : LegalEntityActivationAction.KillSwitch;

        store.IsMultiLegalEntityEnabled = request.IsEnabled;
        store.MultiLegalEntityActivatedAtUtc = request.IsEnabled ? now : null;

        var activationEvent = new LegalEntityActivationEvent
        {
            StoreId = store.Id,
            Action = action,
            PreviousIsEnabled = previousState,
            NewIsEnabled = request.IsEnabled,
            OccurredAtUtc = now,
            ActivationAtUtc = request.IsEnabled ? now : previousActivationAt,
            ChangedByUserId = _currentUser.UserId,
            ChangedByUserName = NormalizeOptional(_currentUser.UserName),
            Reason = reason,
            PreflightPassed = preflight?.IsConfigurationReady == true,
            PreflightSnapshotJson = preflight == null
                ? null
                : JsonSerializer.Serialize(new
                {
                    preflight.ActiveLegalEntityCount,
                    preflight.IsConfigurationReady,
                    preflight.Checks
                })
        };

        await _canaryRepository.AddEventAsync(activationEvent, ct);
        if (!await _canaryRepository.TrySaveChangesAsync(ct))
        {
            return Result<LegalEntityCanaryStateChangeDto>.Failure(Error.Conflict(
                "Trạng thái Multi LegalEntity vừa được người khác thay đổi. Hãy tải lại trước khi thao tác."));
        }

        return Result<LegalEntityCanaryStateChangeDto>.Success(new LegalEntityCanaryStateChangeDto
        {
            IsEnabled = request.IsEnabled,
            WasChanged = true,
            ActivatedAtUtc = store.MultiLegalEntityActivatedAtUtc,
            ActivationEventId = activationEvent.Id,
            Message = request.IsEnabled
                ? "Đã kích hoạt canary Multi LegalEntity. Chỉ order mới sẽ nhận chế độ Multi."
                : "Đã bật kill switch. Order mới quay về legacy; các giỏ Multi đang xử lý giữ nguyên cohort."
        });
    }

    private static DateTime? ResolveMonitoringSince(
        Store store,
        IReadOnlyCollection<LegalEntityActivationEvent> events)
        => store.MultiLegalEntityActivatedAtUtc
            ?? events
                .Where(x => x.NewIsEnabled)
                .OrderByDescending(x => x.OccurredAtUtc)
                .Select(x => (DateTime?)x.OccurredAtUtc)
                .FirstOrDefault();

    private static List<LegalEntityCanaryHealthCheckDto> BuildHealthChecks(
        bool isEnabled,
        LegalEntityActivationPreflightDto preflight,
        LegalEntityCanaryOperationalMetricsDto metrics)
    {
        var checks = new List<LegalEntityCanaryHealthCheckDto>();
        AddCheck(checks, "Canary.Configuration", "Cấu hình không bị drift", "Critical",
            preflight.IsConfigurationReady,
            preflight.IsConfigurationReady
                ? "Preflight hiện tại vẫn đạt."
                : "Cấu hình HKD/kho/hóa đơn đã thay đổi và không còn đạt preflight.");
        AddCheck(checks, "Canary.AllocationMismatch", "Tổng đơn và allocation", "Critical",
            metrics.AllocationMismatchCount == 0,
            metrics.AllocationMismatchCount == 0
                ? "Không có order lệch tổng allocation."
                : $"Có {metrics.AllocationMismatchCount} order lệch tổng allocation.");
        AddCheck(checks, "Canary.InvoiceMismatch", "Hóa đơn theo allocation", "Critical",
            metrics.InvoiceMismatchCount == 0,
            metrics.InvoiceMismatchCount == 0
                ? "Không có order lệch hóa đơn kỳ vọng."
                : $"Có {metrics.InvoiceMismatchCount} order lệch hóa đơn kỳ vọng.");
        AddCheck(checks, "Canary.InventoryIssue", "Inventory issue đang mở", "Warning",
            metrics.OpenInventoryIssueCount == 0,
            metrics.OpenInventoryIssueCount == 0
                ? "Không có issue tồn kho mới đang mở."
                : $"Có {metrics.OpenInventoryIssueCount} issue tồn kho cần xử lý.");
        AddCheck(checks, "Canary.InvoiceFailure", "Phát hành hóa đơn lỗi", "Warning",
            metrics.FailedInvoiceCount == 0,
            metrics.FailedInvoiceCount == 0
                ? "Không có hóa đơn Multi phát hành lỗi."
                : $"Có {metrics.FailedInvoiceCount} hóa đơn phát hành lỗi.");
        AddCheck(checks, "Canary.StaleReservation", "Reservation quá 24 giờ", "Warning",
            metrics.StaleReservationCount == 0,
            metrics.StaleReservationCount == 0
                ? "Không có reservation active quá 24 giờ."
                : $"Có {metrics.StaleReservationCount} reservation active quá 24 giờ.");

        if (!isEnabled && metrics.PendingMultiModeOrderCount > 0)
        {
            AddCheck(checks, "Canary.PendingMultiCohort", "Giỏ Multi đang xử lý", "Warning", false,
                $"Kill switch đang bật nhưng còn {metrics.PendingMultiModeOrderCount} giỏ Multi. Các giỏ này vẫn chốt theo cohort cũ để không sai tồn.");
        }

        return checks;
    }

    private static void AddCheck(
        ICollection<LegalEntityCanaryHealthCheckDto> checks,
        string code,
        string title,
        string level,
        bool passed,
        string message)
        => checks.Add(new LegalEntityCanaryHealthCheckDto
        {
            Code = code,
            Title = title,
            Level = level,
            IsPassed = passed,
            Message = message
        });

    private static string ResolveHealthStatus(
        bool isEnabled,
        IReadOnlyCollection<LegalEntityCanaryHealthCheckDto> checks)
    {
        if (!isEnabled)
            return "Stopped";
        if (checks.Any(x => !x.IsPassed && x.Level == "Critical"))
            return "Critical";
        if (checks.Any(x => !x.IsPassed && x.Level == "Warning"))
            return "Warning";
        return "Healthy";
    }

    private static string BuildHealthMessage(
        string status,
        LegalEntityCanaryOperationalMetricsDto metrics)
        => status switch
        {
            "Healthy" => $"Canary ổn định trên {metrics.AllocatedOrderCount} order allocation.",
            "Warning" => "Canary đang chạy nhưng có cảnh báo vận hành cần theo dõi.",
            "Critical" => "Phát hiện sai lệch trọng yếu. Nên kiểm tra và cân nhắc kill switch.",
            _ => "Multi LegalEntity đang tắt cho order mới."
        };

    private static LegalEntityCanaryEventDto MapEvent(LegalEntityActivationEvent item)
        => new()
        {
            Id = item.Id,
            Action = item.Action.ToString(),
            PreviousIsEnabled = item.PreviousIsEnabled,
            NewIsEnabled = item.NewIsEnabled,
            OccurredAtUtc = item.OccurredAtUtc,
            ActivationAtUtc = item.ActivationAtUtc,
            ChangedByUserId = item.ChangedByUserId,
            ChangedByUserName = item.ChangedByUserName,
            Reason = item.Reason,
            PreflightPassed = item.PreflightPassed
        };

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
