using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public sealed class AutoInvoiceService : IAutoInvoiceService
{
    private const string WorkerName = "auto-invoice-worker";
    private readonly IAutoInvoiceRepository _repository;
    private readonly IInvoiceInputStockRepository _inputStockRepository;
    private readonly IViettelInvoiceIssueService _issueService;
    private readonly IViettelInvoiceSyncService _syncService;
    private readonly IAppUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IInvoiceProviderSettingRepository? _providerSettings;
    private readonly TimeProvider _clock;

    public AutoInvoiceService(
        IAutoInvoiceRepository repository,
        IInvoiceInputStockRepository inputStockRepository,
        IViettelInvoiceIssueService issueService,
        IViettelInvoiceSyncService syncService,
        IAppUnitOfWork unitOfWork,
        ITenantContext tenant,
        ICurrentUser currentUser,
        TimeProvider clock,
        IInvoiceProviderSettingRepository? providerSettings = null)
    {
        _repository = repository;
        _inputStockRepository = inputStockRepository;
        _issueService = issueService;
        _syncService = syncService;
        _unitOfWork = unitOfWork;
        _tenant = tenant;
        _currentUser = currentUser;
        _clock = clock;
        _providerSettings = providerSettings;
    }

    public async Task<AutoInvoiceDashboardDto> GetDashboardAsync(
        AutoInvoiceDashboardQueryDto query,
        CancellationToken ct = default)
    {
        var storeId = RequireStore();
        var settings = await EnsureSettingsAsync(storeId, ct);
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var scope = ResolveScope(settings, query, nowUtc);
        var candidates = await _repository.GetCandidateInvoicesAsync(
            storeId,
            scope.FromUtc,
            scope.ToUtc,
            ct);
        var activeOperations = await _repository.GetActiveOperationsAsync(storeId, ct);
        var operations = await _repository.GetOperationsAsync(storeId, 100, ct);
        var worker = await _repository.GetWorkerStateAsync(storeId, WorkerName, ct);

        var activeSourceIds = activeOperations
            .SelectMany(x => x.Sources)
            .Where(x => x.IsActive)
            .Select(x => x.InvoiceHeadId)
            .ToHashSet();

        // Keep claimed/uncertain invoices visible to Admin. The worker still
        // excludes activeSourceIds when selecting work, but hiding them here
        // made an unissued invoice look as if it had disappeared.
        var visible = candidates
            .OrderBy(SaleAtUtc)
            .ToList();
        var processableVisible = visible
            .Where(x => !activeSourceIds.Contains(x.Id))
            .ToList();

        var workerDto = MapWorker(worker);
        if (workerDto.LastHeartbeatAtUtc.HasValue &&
            workerDto.LastHeartbeatAtUtc.Value < nowUtc.AddSeconds(-Math.Max(30, settings.SendIntervalSeconds * 3)))
            workerDto.IsRunning = false;

        var result = new AutoInvoiceDashboardDto
        {
            Settings = MapSettings(settings),
            Worker = workerDto,
            Summary = new AutoInvoiceDashboardSummaryDto
            {
                TodayCount = candidates.Count(x => SaleDateLocal(x, settings, nowUtc) == LocalNow(settings, nowUtc).Date),
                PendingCount = visible.Count,
                GroupWaitingCount = BuildGroups(processableVisible, settings, nowUtc).Count,
                ErrorCount = visible.Count(x => !string.IsNullOrWhiteSpace(x.LastErrorCode)),
                UnknownCount = visible.Count(IsUnknown),
                IssuedCount = candidates.Count(x => x.ProviderStatus is InvoiceProviderStatus.Issued or InvoiceProviderStatus.PdfDownloaded or InvoiceProviderStatus.ZipDownloaded or InvoiceProviderStatus.EmailSent)
            },
            Queue = visible.Select(x => MapQueueItem(x, settings, nowUtc, activeSourceIds.Contains(x.Id))).ToList(),
            Groups = BuildGroups(processableVisible, settings, nowUtc),
            Errors = BuildErrors(visible, activeOperations),
            History = operations.Select(MapHistory).ToList()
        };

        return result;
    }

    public async Task MarkWorkerStoppedAsync(CancellationToken ct = default)
    {
        var storeIds = _tenant.StoreId.HasValue
            ? new List<int> { RequireStore() }
            : await _repository.GetActiveStoreIdsAsync(ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var storeId in storeIds)
        {
            var state = await _repository.GetWorkerStateAsync(storeId, WorkerName, ct);
            if (state == null)
                continue;
            state.IsRunning = false;
            state.StoppedAtUtc = now;
            state.CurrentOperationId = null;
            state.CurrentInvoiceHeadId = null;
            await _repository.SaveChangesAsync(ct);
        }
    }

    public async Task<Result<AutoInvoiceSettingsDto>> UpdateSettingsAsync(
        UpdateAutoInvoiceSettingsRequest request,
        CancellationToken ct = default)
    {
        var validation = ValidateSettings(request);
        if (!validation.IsSuccess)
            return Result<AutoInvoiceSettingsDto>.Failure(validation.Error!);

        var storeId = RequireStore();
        var settings = await EnsureSettingsAsync(storeId, ct);
        settings.IsEnabled = request.IsEnabled;
        settings.MinimumAgeMinutes = request.MinimumAgeMinutes;
        settings.SeparateAmountThreshold = request.SeparateAmountThreshold;
        settings.GroupTargetAmount = request.GroupTargetAmount;
        settings.SendIntervalSeconds = request.SendIntervalSeconds;
        settings.ClosingTimeLocal = request.ClosingTimeLocal;
        settings.IssueOldDayRemainder = request.IssueOldDayRemainder;
        settings.ScopeMode = request.ScopeMode;
        settings.ScopeStartDateLocal = request.ScopeStartDateLocal?.Date;
        settings.ScopeEndDateLocal = request.ScopeEndDateLocal?.Date;
        settings.TimeZoneId = ResolveTimeZoneId(request.TimeZoneId, settings.TimeZoneId);

        if (settings.IsEnabled)
            settings.LastEnabledAtUtc = _clock.GetUtcNow().UtcDateTime;
        else
            settings.LastDisabledAtUtc = _clock.GetUtcNow().UtcDateTime;

        await ResetWorkerScheduleAsync(storeId, ct);

        await _repository.SaveChangesAsync(ct);
        return Result<AutoInvoiceSettingsDto>.Success(MapSettings(settings));
    }

    public async Task<Result> SetEnabledAsync(
        bool enabled,
        CancellationToken ct = default)
    {
        var storeId = RequireStore();
        var settings = await EnsureSettingsAsync(storeId, ct);
        settings.IsEnabled = enabled;
        var now = _clock.GetUtcNow().UtcDateTime;
        if (enabled)
            settings.LastEnabledAtUtc = now;
        else
            settings.LastDisabledAtUtc = now;

        await ResetWorkerScheduleAsync(storeId, ct);

        await _repository.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<bool>> ToggleEnabledAsync(
        CancellationToken ct = default)
    {
        var storeId = RequireStore();
        var settings = await EnsureSettingsAsync(storeId, ct);
        settings.IsEnabled = !settings.IsEnabled;
        var now = _clock.GetUtcNow().UtcDateTime;

        if (settings.IsEnabled)
            settings.LastEnabledAtUtc = now;
        else
            settings.LastDisabledAtUtc = now;

        await ResetWorkerScheduleAsync(storeId, ct);

        await _repository.SaveChangesAsync(ct);
        return Result<bool>.Success(settings.IsEnabled);
    }

    public async Task<Result> RunOnceAsync(bool force = false, CancellationToken ct = default)
    {
        var storeIds = _tenant.StoreId.HasValue
            ? new List<int> { RequireStore() }
            : await _repository.GetActiveStoreIdsAsync(ct);

        foreach (var storeId in storeIds)
        {
            var settings = await EnsureSettingsAsync(storeId, ct);
            var state = await _repository.GetWorkerStateAsync(storeId, WorkerName, ct);
            if (state == null)
            {
                state = new AutoInvoiceWorkerState
                {
                    StoreId = storeId,
                    WorkerName = WorkerName,
                    WorkerInstanceId = Environment.MachineName,
                    StartedAtUtc = _clock.GetUtcNow().UtcDateTime
                };
                await _repository.AddWorkerStateAsync(state, ct);
            }

            var now = _clock.GetUtcNow().UtcDateTime;
            state.IsRunning = true;
            state.LastHeartbeatAtUtc = now;
            if (!settings.IsEnabled || (!force && state.NextRunAtUtc.HasValue && state.NextRunAtUtc > now))
            {
                state.LastScanAtUtc = now;
                await _repository.SaveChangesAsync(ct);
                continue;
            }

            state.LastScanAtUtc = now;
            state.LastErrorCode = null;
            state.LastErrorMessage = null;
            try
            {
                state.LastResult = "Đã quét queue.";
                var providerHealth = await ValidateActiveProviderCredentialAsync(storeId, ct);
                if (!providerHealth.IsSuccess)
                {
                    state.LastErrorCode = providerHealth.Error?.Code;
                    state.LastErrorMessage = providerHealth.Error?.Message;
                    state.LastResult = "Đã dừng an toàn: cần sửa cấu hình Viettel trước khi phát hành.";
                    state.NextRunAtUtc = now.AddSeconds(Math.Max(1, settings.SendIntervalSeconds));
                    await _repository.SaveChangesAsync(ct);
                    continue;
                }

                var scope = ResolveScope(settings, null, now);
                var reopened = await _repository.ClearRecoverableCredentialErrorsAsync(
                    storeId,
                    scope.FromUtc,
                    scope.ToUtc,
                    ct);
                if (reopened > 0)
                {
                    state.LastResult = $"Đã mở lại {reopened} hóa đơn sau khi xác thực lại credential Viettel.";
                }

                await ProcessStoreOnceAsync(storeId, settings, state, ct);
            }
            catch (Exception ex)
            {
                // A failed transaction can leave Added operation/group entities
                // tracked even though SQL Server rolled them back. Detach them
                // before saving the worker heartbeat/error, otherwise the next
                // SaveChanges retries an orphan FK insert and stops the worker.
                _repository.DiscardFailedAutoInvoiceChanges();
                state.LastErrorCode = ex.GetType().Name;
                state.LastErrorMessage = ex.Message;
                state.LastResult = "Worker gặp lỗi khi quét queue.";
            }
            state.NextRunAtUtc = now.AddSeconds(Math.Max(1, settings.SendIntervalSeconds));
            try
            {
                await _repository.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
            {
                // Another worker instance may have updated the same heartbeat.
                // Drop this stale tracked state and let the next cycle reload it.
                _repository.DiscardFailedAutoInvoiceChanges(includeWorkerState: true);
            }
        }

        return Result.Success();
    }

    public async Task<Result<ViettelInvoiceIssueResultDto>> IssueManualAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        var storeId = RequireStore();
        var operationResult = await CreateSingleOperationAsync(
            storeId,
            invoiceHeadId,
            isManual: true,
            ct);
        if (!operationResult.IsSuccess)
            return Result<ViettelInvoiceIssueResultDto>.Failure(operationResult.Error!);

        var operation = operationResult.Value!;
        var result = await _issueService.IssueAsync(invoiceHeadId, ct);
        await CompleteOperationAsync(operation, result, ct);
        return result;
    }

    public async Task<Result<ViettelInvoiceLookupResultDto>> SyncUnknownAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        var storeId = RequireStore();
        var result = await _syncService.SyncByTransactionUuidAsync(invoiceHeadId, ct);
        var operations = await _repository.GetActiveOperationsAsync(storeId, ct);
        var related = operations
            .Where(x => x.InvoiceHeadId == invoiceHeadId ||
                        x.Sources.Any(s => s.InvoiceHeadId == invoiceHeadId))
            .ToList();

        foreach (var operation in related)
        {
            if (result.IsSuccess && result.Value.IsFound)
            {
                operation.Status = AutoInvoiceOperationStatus.Succeeded;
                operation.ErrorCode = null;
                operation.ErrorMessage = null;
                operation.CompletedAtUtc = _clock.GetUtcNow().UtcDateTime;
                foreach (var source in operation.Sources)
                {
                    source.Status = AutoInvoiceSourceStatus.Succeeded;
                    source.IsActive = false;
                    source.CompletedAtUtc = operation.CompletedAtUtc;
                }
            }
            else if (result.IsSuccess && !result.Value.IsFound)
            {
                operation.Status = AutoInvoiceOperationStatus.Failed;
                operation.ErrorCode = result.Value.ErrorCode ?? "UUID_NOT_FOUND";
                operation.ErrorMessage = result.Value.ErrorMessage;
                operation.CompletedAtUtc = _clock.GetUtcNow().UtcDateTime;
                foreach (var source in operation.Sources)
                {
                    source.Status = AutoInvoiceSourceStatus.Failed;
                    source.IsActive = false;
                }
            }
        }

        await _repository.SaveChangesAsync(ct);
        return result;
    }

    private async Task ProcessStoreOnceAsync(
        int storeId,
        AutoInvoiceSettings settings,
        AutoInvoiceWorkerState state,
        CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var active = await _repository.GetActiveOperationsAsync(storeId, ct);

        // A worker can stop after the durable claim is committed but before
        // the provider call completes. Resume that operation first; otherwise
        // its active sources would permanently hide the original invoices.
        var resumable = active
            .Where(x => x.InvoiceHeadId.HasValue &&
                        x.Sources.Any(s => s.IsActive) &&
                        x.Status is AutoInvoiceOperationStatus.Pending or
                                   AutoInvoiceOperationStatus.Processing or
                                   AutoInvoiceOperationStatus.Unknown)
            .OrderBy(x => x.ClaimedAtUtc ?? x.CreatedAtUtc)
            .FirstOrDefault();
        if (resumable != null)
        {
            state.CurrentOperationId = resumable.Id;
            state.CurrentInvoiceHeadId = resumable.InvoiceHeadId;
            await _repository.SaveChangesAsync(ct);
            try
            {
                await ResumeOperationAsync(storeId, resumable, ct);
            }
            finally
            {
                state.CurrentOperationId = null;
                state.CurrentInvoiceHeadId = null;
            }
            return;
        }

        var scope = ResolveScope(settings, null, nowUtc);
        var candidates = await _repository.GetCandidateInvoicesAsync(storeId, scope.FromUtc, scope.ToUtc, ct);
        await ReopenResolvedStockErrorsAsync(candidates, ct);
        var activeIds = active.SelectMany(x => x.Sources).Where(x => x.IsActive).Select(x => x.InvoiceHeadId).ToHashSet();
        var ready = candidates
            .Where(x => !activeIds.Contains(x.Id))
            // A blocked/failed invoice is visible in Cần xử lý and must not
            // stop the chronological queue. It can be retried only after an
            // its blocking condition is resolved. Stock errors are rechecked
            // above because XML mapping/warehouse setup can be repaired by Admin.
            .Where(x => string.IsNullOrWhiteSpace(x.LastErrorCode))
            // Issuing/IssuedWaitingNumber are uncertain provider states. They
            // must remain visible for UUID lookup, but can never be submitted
            // again automatically.
            .Where(x => !IsUnknown(x))
            .Where(x => SaleAtUtc(x) <= nowUtc.AddMinutes(-settings.MinimumAgeMinutes))
            .OrderBy(SaleAtUtc)
            .ThenBy(x => x.Id)
            .ToList();

        if (ready.Count == 0)
            return;

        var selection = AutoInvoiceOrderingPolicy.Select(
            ready.Select(x => new AutoInvoiceCandidate(
                x.Id,
                SaleAtUtc(x),
                SaleDateLocal(x, settings, nowUtc),
                x.GrandTotal,
                IsConsumer(x),
                BuildGroupKey(x, settings, nowUtc))).ToList(),
            settings.SeparateAmountThreshold,
            settings.GroupTargetAmount,
            settings.ClosingTimeLocal,
            settings.IssueOldDayRemainder,
            LocalNow(settings, nowUtc));

        if (selection == null)
            return;

        if (selection.Kind == AutoInvoiceOperationKind.Single)
        {
            var single = ready.First(x => x.Id == selection.InvoiceHeadIds[0]);
            await ProcessSingleAsync(storeId, single, ct);
            return;
        }

        var group = ready
            .Where(x => selection.InvoiceHeadIds.Contains(x.Id))
            .OrderBy(SaleAtUtc)
            .ThenBy(x => x.Id)
            .ToList();
        if (group.Count == 0)
            return;

        await ProcessGroupAsync(storeId, group, settings, ct);
    }

    private async Task ReopenResolvedStockErrorsAsync(
        IReadOnlyCollection<InvoiceHead> candidates,
        CancellationToken ct)
    {
        foreach (var invoice in candidates.Where(x =>
                     string.Equals(
                         x.LastErrorCode,
                         "Invoice.InputInvoiceStockInsufficient",
                         StringComparison.OrdinalIgnoreCase)))
        {
            var availability = await _inputStockRepository.GetAvailabilityAsync(invoice.Id, ct);
            if (!availability.IsSufficient)
                continue;

            // Candidates are intentionally read without tracking. Clearing the
            // in-memory error is enough to re-enter this cycle; the issuance
            // transaction persists the clean state when it claims the draft.
            invoice.LastErrorCode = null;
            invoice.LastErrorMessage = null;
        }
    }

    private async Task ResumeOperationAsync(
        int storeId,
        AutoInvoiceOperation operation,
        CancellationToken ct)
    {
        var invoice = await _repository.GetInvoiceHeadForAutomaticIssueAsync(
            storeId,
            operation.InvoiceHeadId!.Value,
            ct);
        if (invoice == null)
        {
            operation.Status = AutoInvoiceOperationStatus.Failed;
            operation.ErrorCode = "Invoice.NotFound";
            operation.ErrorMessage = "Không tìm thấy InvoiceHead của operation cần tiếp tục.";
            operation.CompletedAtUtc = _clock.GetUtcNow().UtcDateTime;
            foreach (var source in operation.Sources)
            {
                source.Status = AutoInvoiceSourceStatus.Failed;
                source.IsActive = false;
                source.ErrorCode = operation.ErrorCode;
                source.ErrorMessage = operation.ErrorMessage;
                source.CompletedAtUtc = operation.CompletedAtUtc;
            }
            await _repository.SaveChangesAsync(ct);
            return;
        }

        if (invoice.ProviderStatus is InvoiceProviderStatus.Issued or
            InvoiceProviderStatus.PdfDownloaded or
            InvoiceProviderStatus.ZipDownloaded or
            InvoiceProviderStatus.EmailSent)
        {
            await CompleteOperationAsync(
                operation,
                Result<ViettelInvoiceIssueResultDto>.Success(new ViettelInvoiceIssueResultDto
                {
                    InvoiceHeadId = invoice.Id,
                    IsSuccess = true,
                    InvoiceNo = invoice.ProviderInvoiceNo
                }),
                ct);
            return;
        }

        if (IsUnknown(invoice))
        {
            // A provider call may already have reached Viettel. Lookup first;
            // never submit an Issuing/IssuedWaitingNumber invoice blindly.
            var lookup = await SyncUnknownAsync(invoice.Id, ct);
            if (!lookup.IsSuccess)
            {
                if (lookup.Error?.Code == "InvoiceProvider.CredentialKeyUnavailable" ||
                    lookup.Error?.Code == "InvoiceProvider.NotConfigured")
                {
                    await FailOperationAsync(operation, lookup.Error, ct);
                }
                return;
            }

            if (lookup.Value.IsFound)
                return;

            // Viettel explicitly confirmed that the UUID is not present. The
            // same UUID can now be submitted again safely; it remains the
            // idempotency key for this invoice.
            operation.Status = AutoInvoiceOperationStatus.Processing;
            operation.ErrorCode = null;
            operation.ErrorMessage = null;
            operation.CompletedAtUtc = null;
            operation.AttemptCount++;
            foreach (var source in operation.Sources)
            {
                source.Status = AutoInvoiceSourceStatus.Claimed;
                source.IsActive = true;
                source.ErrorCode = null;
                source.ErrorMessage = null;
                source.CompletedAtUtc = null;
            }
            await _repository.SaveChangesAsync(ct);

            var retryResult = await IssueSafelyAsync(invoice.Id, ct);
            await CompleteOperationAsync(operation, retryResult, ct);
            return;
        }

        operation.Status = AutoInvoiceOperationStatus.Processing;
        operation.AttemptCount++;
        await _repository.SaveChangesAsync(ct);

        var result = await IssueSafelyAsync(invoice.Id, ct);
        await CompleteOperationAsync(operation, result, ct);
    }

    private async Task FailOperationAsync(
        AutoInvoiceOperation operation,
        Error error,
        CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        operation.Status = AutoInvoiceOperationStatus.Failed;
        operation.ErrorCode = error.Code;
        operation.ErrorMessage = error.Message;
        operation.CompletedAtUtc = now;
        foreach (var source in operation.Sources)
        {
            source.Status = AutoInvoiceSourceStatus.Failed;
            source.IsActive = false;
            source.ErrorCode = error.Code;
            source.ErrorMessage = error.Message;
            source.CompletedAtUtc = now;
        }

        await _repository.SaveChangesAsync(ct);
    }

    private async Task ProcessSingleAsync(
        int storeId,
        InvoiceHead invoice,
        CancellationToken ct)
    {
        var preflight = ValidateInvoiceForAutomaticIssue(invoice);
        if (!preflight.IsSuccess)
        {
            await RecordBlockedSingleAsync(storeId, invoice, preflight.Error!, ct);
            return;
        }

        var operationResult = await CreateSingleOperationAsync(storeId, invoice.Id, false, ct);
        if (!operationResult.IsSuccess)
            return;

        var operation = operationResult.Value!;
        operation.SaleDateLocal = SaleDateLocal(invoice, await EnsureSettingsAsync(storeId, ct), _clock.GetUtcNow().UtcDateTime);
        var result = await IssueSafelyAsync(invoice.Id, ct);
        await CompleteOperationAsync(operation, result, ct);
    }

    private async Task ProcessGroupAsync(
        int storeId,
        IReadOnlyList<InvoiceHead> invoices,
        AutoInvoiceSettings settings,
        CancellationToken ct)
    {
        foreach (var invoice in invoices)
        {
            var validation = ValidateInvoiceForAutomaticIssue(invoice);
            if (!validation.IsSuccess)
            {
                await RecordBlockedSingleAsync(storeId, invoice, validation.Error!, ct);
                return;
            }

            var availability = await _inputStockRepository.GetAvailabilityAsync(invoice.Id, ct);
            if (!availability.IsSufficient)
            {
                await RecordBlockedSingleAsync(
                    storeId,
                    invoice,
                    Error.Validation(
                        "Invoice.InputInvoiceStockInsufficient",
                        BuildShortageMessage(availability)),
                    ct);
                return;
            }
        }

        var groupInvoice = BuildGroupInvoice(storeId, invoices, settings);
        var groupKey = BuildGroupKey(invoices[0], settings, _clock.GetUtcNow().UtcDateTime);
        var operation = new AutoInvoiceOperation
        {
            StoreId = storeId,
            Kind = AutoInvoiceOperationKind.Group,
            Status = AutoInvoiceOperationStatus.Pending,
            SaleDateLocal = SaleDateLocal(invoices[0], settings, _clock.GetUtcNow().UtcDateTime),
            GroupKey = groupKey,
            IsManual = false,
            Sources = invoices.Select(invoice =>
                {
                    var details = invoice.Details
                        .Where(x => !x.IsDeleted)
                        .ToList();
                    var firstDetail = details.FirstOrDefault();
                    return new AutoInvoiceOperationSource
                    {
                        StoreId = storeId,
                        InvoiceHeadId = invoice.Id,
                        OrderId = invoice.OrderId,
                        InvoiceDetailId = firstDetail?.Id,
                        OrderLineId = firstDetail?.OrderLineId,
                        Status = AutoInvoiceSourceStatus.Pending,
                        IsActive = true,
                        SourceSnapshotJson = JsonSerializer.Serialize(new
                        {
                            InvoiceId = invoice.Id,
                            invoice.OrderId,
                            Details = details.Select(detail => new
                            {
                                DetailId = detail.Id,
                                detail.OrderLineId,
                                detail.ItemName,
                                detail.UnitName,
                                detail.Quantity,
                                detail.TotalAmount
                            }).ToList()
                        })
                    };
                })
                .ToList()
        };

        try
        {
            await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
            await _repository.AddInvoiceHeadAsync(groupInvoice, ct);
            await _repository.SaveChangesAsync(ct);
            operation.InvoiceHeadId = groupInvoice.Id;
            await _repository.AddOperationAsync(operation, ct);
            await _repository.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex) when (IsActiveSourceConflict(ex))
        {
            // Another worker already claimed one of the invoices, or this
            // operation contained duplicate source rows. It is safe to skip;
            // the next cycle will re-read the queue.
            _repository.DiscardFailedAutoInvoiceChanges();
            return;
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateException" || ex is InvalidOperationException)
        {
            _repository.DiscardFailedAutoInvoiceChanges();
            throw;
        }

        var result = await IssueSafelyAsync(groupInvoice.Id, ct);
        await CompleteOperationAsync(operation, result, ct);
    }

    private async Task<Result<ViettelInvoiceIssueResultDto>> IssueSafelyAsync(
        int invoiceHeadId,
        CancellationToken ct)
    {
        try
        {
            return await _issueService.IssueAsync(invoiceHeadId, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("TIMEOUT", "Kết nối Viettel hết thời gian chờ; cần tra cứu UUID trước khi gửi lại."));
        }
        catch (TimeoutException ex)
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("TIMEOUT", $"Viettel hết thời gian chờ: {ex.Message}"));
        }
        catch (HttpRequestException ex)
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("UNKNOWN_RESULT", $"Không xác định được kết quả Viettel: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("UNKNOWN_RESULT", $"Không xác định được kết quả Viettel: {ex.Message}"));
        }
    }

    private async Task<Result<AutoInvoiceOperation>> CreateSingleOperationAsync(
        int storeId,
        int invoiceHeadId,
        bool isManual,
        CancellationToken ct)
    {
        if (invoiceHeadId <= 0)
            return Result<AutoInvoiceOperation>.Failure(Error.Validation("Invoice.InvalidInvoiceHeadId", "InvoiceHeadId không hợp lệ."));

        if (await _repository.HasActiveSourceAsync(storeId, invoiceHeadId, ct))
            return Result<AutoInvoiceOperation>.Failure(Error.Conflict("Hóa đơn đang được phát hành bởi một thao tác khác."));

        var operation = new AutoInvoiceOperation
        {
            StoreId = storeId,
            Kind = AutoInvoiceOperationKind.Single,
            Status = AutoInvoiceOperationStatus.Processing,
            InvoiceHeadId = invoiceHeadId,
            IsManual = isManual,
            RequestedByUserId = _currentUser.UserId,
            RequestedByUserName = _currentUser.UserName,
            ClaimedAtUtc = _clock.GetUtcNow().UtcDateTime,
            AttemptCount = 1,
            Sources = new List<AutoInvoiceOperationSource>
            {
                new()
                {
                    StoreId = storeId,
                    InvoiceHeadId = invoiceHeadId,
                    Status = AutoInvoiceSourceStatus.Claimed,
                    IsActive = true
                }
            }
        };

        try
        {
            await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
            await _repository.AddOperationAsync(operation, ct);
            await _repository.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result<AutoInvoiceOperation>.Success(operation);
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateException")
        {
            _repository.DiscardFailedAutoInvoiceChanges();
            return Result<AutoInvoiceOperation>.Failure(Error.Conflict("Hóa đơn vừa được thao tác bởi một yêu cầu khác."));
        }
    }

    private async Task CompleteOperationAsync(
        AutoInvoiceOperation operation,
        Result<ViettelInvoiceIssueResultDto> result,
        CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var uncertain = !result.IsSuccess && IsUncertain(result.Error?.Code, result.Error?.Message);
        operation.Status = result.IsSuccess
            ? AutoInvoiceOperationStatus.Succeeded
            : uncertain
                ? AutoInvoiceOperationStatus.Unknown
                : AutoInvoiceOperationStatus.Failed;
        operation.SubmittedAtUtc ??= now;
        operation.CompletedAtUtc = result.IsSuccess || !uncertain ? now : null;
        operation.ErrorCode = result.IsSuccess ? null : result.Error?.Code;
        operation.ErrorMessage = result.IsSuccess ? null : result.Error?.Message;
        foreach (var source in operation.Sources)
        {
            source.Status = result.IsSuccess
                ? AutoInvoiceSourceStatus.Succeeded
                : uncertain
                    ? AutoInvoiceSourceStatus.Unknown
                    : AutoInvoiceSourceStatus.Failed;
            source.IsActive = uncertain ? true : false;
            source.ErrorCode = operation.ErrorCode;
            source.ErrorMessage = operation.ErrorMessage;
            source.CompletedAtUtc = result.IsSuccess || !uncertain ? now : null;
        }

        if (!result.IsSuccess && !uncertain)
        {
            // A group provider failure belongs to every source draft, not
            // only to the synthetic group InvoiceHead. This moves the whole
            // group to Cần xử lý and lets the next chronological invoice run.
            await _repository.MarkInvoiceErrorsAsync(
                operation.StoreId,
                operation.Sources.Select(x => x.InvoiceHeadId).ToArray(),
                operation.ErrorCode ?? "AUTO_INVOICE_FAILED",
                operation.ErrorMessage,
                ct);
        }

        await _repository.SaveChangesAsync(ct);
    }

    private async Task RecordBlockedSingleAsync(
        int storeId,
        InvoiceHead invoice,
        Error error,
        CancellationToken ct)
    {
        var operation = new AutoInvoiceOperation
        {
            StoreId = storeId,
            Kind = AutoInvoiceOperationKind.Single,
            Status = AutoInvoiceOperationStatus.Blocked,
            InvoiceHeadId = invoice.Id,
            SaleDateLocal = invoice.InvoiceDate.Date,
            ErrorCode = error.Code,
            ErrorMessage = error.Message,
            CompletedAtUtc = _clock.GetUtcNow().UtcDateTime,
            Sources = new List<AutoInvoiceOperationSource>
            {
                new()
                {
                    StoreId = storeId,
                    InvoiceHeadId = invoice.Id,
                    Status = AutoInvoiceSourceStatus.Failed,
                    IsActive = false,
                    ErrorCode = error.Code,
                    ErrorMessage = error.Message,
                    CompletedAtUtc = _clock.GetUtcNow().UtcDateTime
                }
            }
        };
        invoice.LastErrorCode = error.Code;
        invoice.LastErrorMessage = error.Message;
        await _repository.AddOperationAsync(operation, ct);
        await _repository.SaveChangesAsync(ct);
    }

    private static Result<bool> ValidateInvoiceForAutomaticIssue(InvoiceHead invoice)
    {
        if (invoice.InvoiceProviderSettingId == null ||
            invoice.InvoiceProviderSetting == null ||
            !invoice.InvoiceProviderSetting.IsActive ||
            invoice.InvoiceProviderSetting.IsDeleted ||
            !string.Equals(invoice.InvoiceProviderSetting.ProviderCode, "VIETTEL", StringComparison.OrdinalIgnoreCase))
        {
            return Result<bool>.Failure(Error.Validation("InvoiceProvider.NotConfigured", "Hóa đơn chưa có cấu hình Viettel hợp lệ."));
        }

        var missingUnit = invoice.Details
            .Where(x => !x.IsDeleted)
            .FirstOrDefault(x => string.IsNullOrWhiteSpace(x.UnitName));
        if (missingUnit != null)
        {
            return Result<bool>.Failure(Error.Validation(
                "Invoice.UnitMissing",
                $"Hóa đơn #{invoice.Id} thiếu đơn vị tính ở dòng '{missingUnit.ItemName}'."));
        }

        return Result<bool>.Success(true);
    }

    private static string BuildShortageMessage(InvoiceInputStockAvailabilityDto availability)
    {
        var shortages = availability.Lines
            .Where(x => !x.IsSufficient)
            .Take(10)
            .Select(x =>
                $"{x.ItemName}: cần {x.RequiredBaseQuantity:0.####}, có {Math.Max(0m, x.AvailableBaseQuantity):0.####}, thiếu {x.ShortageBaseQuantity:0.####} tại {FormatWarehouse(x.WarehouseId)}")
            .ToList();
        return "Tồn hóa đơn đầu vào không đủ: " + string.Join("; ", shortages);
    }

    private static string FormatWarehouse(int warehouseId)
        => warehouseId > 0 ? $"kho #{warehouseId}" : "kho bán chưa được xác định";

    private static InvoiceHead BuildGroupInvoice(
        int storeId,
        IReadOnlyList<InvoiceHead> invoices,
        AutoInvoiceSettings settings)
    {
        var first = invoices[0];
        var details = invoices
            .SelectMany(x => x.Details.Where(d => !d.IsDeleted))
            .Select(d => new InvoiceDetail
            {
                StoreId = storeId,
                ProductVariantId = d.ProductVariantId,
                OrderLineId = d.OrderLineId,
                OrderLegalEntityAllocationId = d.OrderLegalEntityAllocationId,
                LegacyUnitFactor = d.LegacyUnitFactor,
                SourceType = InvoiceDetailSourceType.FromOrderLine,
                ItemName = d.ItemName,
                UnitName = d.UnitName,
                Quantity = d.Quantity,
                UnitPrice = d.UnitPrice,
                Amount = d.Amount,
                VatRate = d.VatRate,
                VatAmount = d.VatAmount,
                TotalAmount = d.TotalAmount,
                Note = $"Nguồn AutoInvoice: InvoiceHeadId={d.InvoiceHeadId}; InvoiceDetailId={d.Id}."
            })
            .ToList();

        return new InvoiceHead
        {
            StoreId = storeId,
            IsAutoInvoiceGroup = true,
            LegalEntityId = first.LegalEntityId,
            InvoiceProviderSettingId = first.InvoiceProviderSettingId,
            InvoiceDate = first.InvoiceDate,
            BuyerType = InvoiceBuyerTypes.NoInvoice,
            TotalQuantity = details.Sum(x => x.Quantity),
            SubTotal = details.Sum(x => x.Amount),
            VatAmount = details.Sum(x => x.VatAmount),
            GrandTotal = details.Sum(x => x.TotalAmount),
            ProviderCode = first.ProviderCode ?? "VIETTEL",
            SupplierTaxCode = first.SupplierTaxCode,
            InvoiceType = first.InvoiceType,
            TemplateCode = first.TemplateCode,
            InvoiceSeries = first.InvoiceSeries,
            Note = "Hóa đơn gộp tự động; nguồn được lưu trong AutoInvoiceOperationSources.",
            Details = details
        };
    }

    private List<AutoInvoiceGroupDto> BuildGroups(
        IReadOnlyList<InvoiceHead> invoices,
        AutoInvoiceSettings settings,
        DateTime nowUtc)
    {
        return invoices
            .Where(IsConsumer)
            .Where(x => string.IsNullOrWhiteSpace(x.LastErrorCode) && !IsUnknown(x))
            .GroupBy(x => BuildGroupKey(x, settings, nowUtc))
            .Select(g =>
            {
                var first = g.OrderBy(SaleAtUtc).First();
                var total = g.Sum(x => x.GrandTotal);
                var date = SaleDateLocal(first, settings, nowUtc).Date;
                var today = LocalNow(settings, nowUtc).Date;
                var closing = date == today && LocalNow(settings, nowUtc).TimeOfDay >= settings.ClosingTimeLocal;
                return new AutoInvoiceGroupDto
                {
                    GroupKey = g.Key,
                    SaleDateLocal = date,
                    LegalEntityId = first.LegalEntityId,
                    InvoiceProviderSettingId = first.InvoiceProviderSettingId,
                    TotalAmount = total,
                    IsReadyByTarget = total >= settings.GroupTargetAmount,
                    IsReadyByClosing = closing || (date < today && settings.IssueOldDayRemainder),
                    InvoiceHeadIds = g.OrderBy(SaleAtUtc).Select(x => x.Id).ToList()
                };
            })
            .OrderBy(x => x.SaleDateLocal)
            .ThenBy(x => x.GroupKey)
            .ToList();
    }

    private static List<AutoInvoiceErrorDto> BuildErrors(
        IReadOnlyList<InvoiceHead> candidates,
        IReadOnlyList<AutoInvoiceOperation> activeOperations)
    {
        var operationErrors = activeOperations
            .Where(x => x.Status == AutoInvoiceOperationStatus.Unknown)
            .SelectMany(x => x.Sources.Select(s => new AutoInvoiceErrorDto
            {
                InvoiceHeadId = s.InvoiceHeadId,
                OperationId = x.Id,
                ErrorCode = x.ErrorCode ?? "UNKNOWN_RESULT",
                ErrorMessage = x.ErrorMessage ?? "Kết quả phát hành chưa xác định; cần tra cứu UUID.",
                RequiresUuidLookup = true,
                UpdatedAtUtc = x.ClaimedAtUtc
            }))
            .ToList();

        operationErrors.AddRange(candidates
            .Where(x => !string.IsNullOrWhiteSpace(x.LastErrorCode))
            .Select(x => new AutoInvoiceErrorDto
            {
                InvoiceHeadId = x.Id,
                OrderNumber = x.Order?.OrderNumber,
                ErrorCode = x.LastErrorCode!,
                ErrorMessage = x.LastErrorMessage ?? string.Empty,
                RequiresUuidLookup = IsUncertain(x.LastErrorCode, x.LastErrorMessage),
                UpdatedAtUtc = x.LastSyncedAtUtc
            }));

        return operationErrors
            .GroupBy(x => new { x.InvoiceHeadId, x.ErrorCode })
            .Select(x => x.First())
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToList();
    }

    private static AutoInvoiceHistoryDto MapHistory(AutoInvoiceOperation operation)
        => new()
        {
            OperationId = operation.Id,
            Kind = operation.Kind,
            Status = operation.Status,
            SaleDateLocal = operation.SaleDateLocal,
            SourceCount = operation.Sources.Count,
            InvoiceHeadId = operation.InvoiceHeadId,
            ErrorCode = operation.ErrorCode,
            ErrorMessage = operation.ErrorMessage,
            CompletedAtUtc = operation.CompletedAtUtc,
            IsManual = operation.IsManual,
            RequestedByUserName = operation.RequestedByUserName
        };

    private static AutoInvoiceQueueItemDto MapQueueItem(
        InvoiceHead invoice,
        AutoInvoiceSettings settings,
        DateTime nowUtc,
        bool hasActiveOperation)
    {
        var status = hasActiveOperation
            ? "Đang xử lý"
            : invoice.LastErrorCode switch
        {
            "Invoice.UnitMissing" => "Thiếu đơn vị tính",
            "Invoice.InputInvoiceStockInsufficient" => "Thiếu tồn hóa đơn",
            "InvoiceProvider.NotConfigured" => "Thiếu cấu hình hóa đơn",
            "InvoiceProvider.CredentialKeyUnavailable" => "Lỗi khóa bảo mật Viettel",
            _ when IsUnknown(invoice) => "Chưa xác định — cần tra cứu UUID",
            _ when !string.IsNullOrWhiteSpace(invoice.LastErrorCode) => "Cần xử lý",
            _ when SaleAtUtc(invoice) > nowUtc.AddMinutes(-settings.MinimumAgeMinutes) => "Chờ tới giờ phát hành",
            _ when IsConsumer(invoice) && invoice.GrandTotal < settings.SeparateAmountThreshold => "Chờ gộp",
            _ => "Sẵn sàng"
        };
        return new AutoInvoiceQueueItemDto
        {
            InvoiceHeadId = invoice.Id,
            OrderId = invoice.OrderId,
            OrderNumber = invoice.Order?.OrderNumber,
            SaleAtUtc = SaleAtUtc(invoice),
            SaleDateLocal = SaleDateLocal(invoice, settings, nowUtc),
            BuyerType = invoice.BuyerType,
            BuyerDisplay = invoice.BuyerType == InvoiceBuyerTypes.NoInvoice
                ? "Bán cho người tiêu dùng"
                : invoice.BuyerLegalName ?? invoice.BuyerName ?? invoice.BuyerTaxCode,
            GrandTotal = invoice.GrandTotal,
            ProviderStatus = invoice.ProviderStatus,
            StatusName = status,
            StoreName = invoice.Store?.Name,
            LegalEntityId = invoice.LegalEntityId,
            InvoiceProviderSettingId = invoice.InvoiceProviderSettingId,
            ProviderCode = invoice.ProviderCode,
            ErrorCode = invoice.LastErrorCode,
            ErrorMessage = invoice.LastErrorMessage,
            IsGroupedConsumer = IsConsumer(invoice) && invoice.GrandTotal < settings.SeparateAmountThreshold
        };
    }

    private static AutoInvoiceSettingsDto MapSettings(AutoInvoiceSettings x)
        => new()
        {
            StoreId = x.StoreId,
            IsEnabled = x.IsEnabled,
            MinimumAgeMinutes = x.MinimumAgeMinutes,
            SeparateAmountThreshold = x.SeparateAmountThreshold,
            GroupTargetAmount = x.GroupTargetAmount,
            SendIntervalSeconds = x.SendIntervalSeconds,
            ClosingTimeLocal = x.ClosingTimeLocal,
            IssueOldDayRemainder = x.IssueOldDayRemainder,
            ScopeMode = x.ScopeMode,
            ScopeStartDateLocal = x.ScopeStartDateLocal,
            ScopeEndDateLocal = x.ScopeEndDateLocal,
            TimeZoneId = x.TimeZoneId,
            UpdatedAtUtc = x.UpdatedAtUtc,
            UpdatedBy = x.UpdatedBy
        };

    private static AutoInvoiceWorkerDto MapWorker(AutoInvoiceWorkerState? x)
        => x == null
            ? new() { WorkerName = WorkerName }
            : new()
            {
                IsRunning = x.IsRunning,
                WorkerName = x.WorkerName,
                WorkerInstanceId = x.WorkerInstanceId,
                StartedAtUtc = x.StartedAtUtc,
                LastHeartbeatAtUtc = x.LastHeartbeatAtUtc,
                LastScanAtUtc = x.LastScanAtUtc,
                CurrentOperationId = x.CurrentOperationId,
                CurrentInvoiceHeadId = x.CurrentInvoiceHeadId,
                NextRunAtUtc = x.NextRunAtUtc,
                LastErrorCode = x.LastErrorCode,
                LastErrorMessage = x.LastErrorMessage,
                LastResult = x.LastResult
            };

    private async Task<AutoInvoiceSettings> EnsureSettingsAsync(int storeId, CancellationToken ct)
    {
        var settings = await _repository.GetSettingsAsync(storeId, ct);
        if (settings != null)
            return settings;

        settings = new AutoInvoiceSettings { StoreId = storeId };
        await _repository.AddSettingsAsync(settings, ct);
        await _repository.SaveChangesAsync(ct);
        return settings;
    }

    private async Task ResetWorkerScheduleAsync(int storeId, CancellationToken ct)
    {
        var state = await _repository.GetWorkerStateAsync(storeId, WorkerName, ct);
        if (state == null)
            return;

        // A configuration change is an explicit wake-up request. The next
        // worker tick must re-read the queue immediately instead of waiting
        // for the previous interval that belonged to the old configuration.
        state.NextRunAtUtc = null;
        state.LastErrorCode = null;
        state.LastErrorMessage = null;
    }

    private async Task<Result> ValidateActiveProviderCredentialAsync(
        int storeId,
        CancellationToken ct)
    {
        // Unit tests and manual in-memory adapters may not provide the
        // provider repository. Production always registers it through DI.
        if (_providerSettings == null)
            return Result.Success();

        try
        {
            var setting = await _providerSettings.GetActiveViettelAsync(storeId, ct);
            if (setting == null)
            {
                return Result.Failure(Error.Validation(
                    "InvoiceProvider.NotConfigured",
                    $"Store #{storeId} chưa có cấu hình Viettel đang hoạt động."));
            }

            return Result.Success();
        }
        catch (CryptographicException ex)
        {
            return Result.Failure(Error.Validation(
                "InvoiceProvider.CredentialKeyUnavailable",
                "Không giải mã được mật khẩu Viettel của cấu hình đang hoạt động. " +
                "Hãy vào Sửa cấu hình, nhập lại mật khẩu Viettel và bấm Lưu; " +
                "sau đó worker sẽ chạy lại bằng key hiện tại. " +
                $"Chi tiết kỹ thuật: {ex.Message}"));
        }
    }

    private int RequireStore()
        => _tenant.StoreId.GetValueOrDefault() > 0
            ? _tenant.StoreId!.Value
            : throw new InvalidOperationException("AutoInvoice yêu cầu tenant Store hiện tại.");

    private static Result<bool> ValidateSettings(UpdateAutoInvoiceSettingsRequest request)
    {
        if (request.MinimumAgeMinutes < 0)
            return Result<bool>.Failure(Error.Validation("AutoInvoice.MinimumAgeInvalid", "Thời gian chờ không được âm."));
        if (request.SeparateAmountThreshold <= 0 || request.GroupTargetAmount <= 0)
            return Result<bool>.Failure(Error.Validation("AutoInvoice.AmountInvalid", "Ngưỡng phát hành và tổng mục tiêu phải lớn hơn 0."));
        if (request.SendIntervalSeconds <= 0)
            return Result<bool>.Failure(Error.Validation("AutoInvoice.IntervalInvalid", "Khoảng cách gửi phải lớn hơn 0 giây."));
        if (request.ClosingTimeLocal < TimeSpan.Zero || request.ClosingTimeLocal >= TimeSpan.FromDays(1))
            return Result<bool>.Failure(Error.Validation("AutoInvoice.ClosingTimeInvalid", "Giờ chốt không hợp lệ."));
        if (request.ScopeMode == AutoInvoiceScopeMode.Range &&
            (!request.ScopeStartDateLocal.HasValue || !request.ScopeEndDateLocal.HasValue || request.ScopeEndDateLocal < request.ScopeStartDateLocal))
            return Result<bool>.Failure(Error.Validation("AutoInvoice.ScopeInvalid", "Phạm vi ngày không hợp lệ."));
        return Result<bool>.Success(true);
    }

    private static string ResolveTimeZoneId(string? requested, string current)
    {
        var id = string.IsNullOrWhiteSpace(requested) ? current : requested.Trim();
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(id);
            return id;
        }
        catch
        {
            return TimeZoneInfo.Local.Id;
        }
    }

    private static (DateTime FromUtc, DateTime ToUtc) ResolveScope(
        AutoInvoiceSettings settings,
        AutoInvoiceDashboardQueryDto? query,
        DateTime nowUtc)
    {
        var zone = GetZone(settings.TimeZoneId);
        var today = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date;
        var mode = query?.ScopeMode ?? settings.ScopeMode;
        var start = query?.StartDateLocal?.Date ?? settings.ScopeStartDateLocal?.Date;
        var end = query?.EndDateLocal?.Date ?? settings.ScopeEndDateLocal?.Date;
        if (mode == AutoInvoiceScopeMode.Month && start.HasValue)
            end = start.Value.AddMonths(1);
        else if (mode == AutoInvoiceScopeMode.Today || !start.HasValue)
        {
            start = today;
            end = today.AddDays(1);
        }
        else if (!end.HasValue)
            end = start.Value;

        if (mode == AutoInvoiceScopeMode.Range && end.HasValue)
            end = end.Value.Date.AddDays(1);

        return (
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(start.Value, DateTimeKind.Unspecified), zone),
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(end.Value, DateTimeKind.Unspecified), zone));
    }

    private static TimeZoneInfo GetZone(string? id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? TimeZoneInfo.Local.Id : id);
        }
        catch
        {
            return TimeZoneInfo.Local;
        }
    }

    private static DateTime LocalNow(AutoInvoiceSettings settings, DateTime nowUtc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), GetZone(settings.TimeZoneId));

    private static DateTime SaleAtUtc(InvoiceHead invoice)
        => invoice.Order?.CompletedAtUtc ?? DateTime.SpecifyKind(invoice.InvoiceDate, DateTimeKind.Utc);

    private static DateTime SaleDateLocal(InvoiceHead invoice, AutoInvoiceSettings settings, DateTime nowUtc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(SaleAtUtc(invoice), DateTimeKind.Utc), GetZone(settings.TimeZoneId));

    private static bool IsConsumer(InvoiceHead invoice)
        => string.Equals(invoice.BuyerType, InvoiceBuyerTypes.NoInvoice, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnknown(InvoiceHead invoice)
        => invoice.ProviderStatus == InvoiceProviderStatus.Issuing ||
           invoice.ProviderStatus == InvoiceProviderStatus.IssuedWaitingNumber ||
           IsUncertain(invoice.LastErrorCode, invoice.LastErrorMessage);

    private static bool IsUncertain(string? code, string? message)
        => string.Equals(code, "TIMEOUT", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(code, "UNKNOWN_RESULT", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(code, "HTTP_500", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(code, "VIETTEL_SERVER_500", StringComparison.OrdinalIgnoreCase) ||
           (!string.IsNullOrWhiteSpace(code) && code.StartsWith("HTTP_5", StringComparison.OrdinalIgnoreCase)) ||
           (!string.IsNullOrWhiteSpace(message) &&
            (message.Contains("timeout", StringComparison.OrdinalIgnoreCase) || message.Contains("HTTP 500", StringComparison.OrdinalIgnoreCase)));

    private static bool IsActiveSourceConflict(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("UX_AutoInvoiceOperationSources_ActiveInvoice", StringComparison.OrdinalIgnoreCase) ||
                (message.Contains("AutoInvoiceOperationSources", StringComparison.OrdinalIgnoreCase) &&
                 message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildGroupKey(InvoiceHead invoice, AutoInvoiceSettings settings, DateTime nowUtc)
    {
        var warehouses = invoice.Details
            .Where(x => !x.IsDeleted)
            .Select(x => x.OrderLegalEntityAllocation?.WarehouseId ?? 0)
            .Distinct()
            .OrderBy(x => x)
            .Select(x => x.ToString(CultureInfo.InvariantCulture));
        return string.Join(
            "|",
            SaleDateLocal(invoice, settings, nowUtc).ToString("yyyy-MM-dd"),
            invoice.StoreId,
            invoice.LegalEntityId ?? 0,
            invoice.InvoiceProviderSettingId ?? 0,
            invoice.ProviderCode ?? string.Empty,
            invoice.SupplierTaxCode ?? string.Empty,
            invoice.InvoiceType ?? string.Empty,
            invoice.TemplateCode ?? string.Empty,
            invoice.InvoiceSeries ?? string.Empty,
            string.Join(",", warehouses));
    }
}
