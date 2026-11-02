using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Invoices;

public class InvoiceIntegrationLogRepository : IInvoiceIntegrationLogRepository
{
    private readonly AppDbContext _db;

    public InvoiceIntegrationLogRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(
        InvoiceIntegrationLog log,
        CancellationToken ct = default)
    {
        // Provider clients also run without a request tenant. Derive log ownership
        // from the persisted invoice rather than a missing/default StoreId.
        var storeId = await _db.InvoiceHeads.AsNoTracking()
            .Where(x => x.Id == log.InvoiceHeadId && !x.IsDeleted)
            .Select(x => (int?)x.StoreId)
            .SingleOrDefaultAsync(ct);
        if (storeId is null or <= 0 || (log.StoreId != 0 && log.StoreId != storeId.Value))
            throw new InvalidOperationException("Integration log invoice/store ownership is invalid.");

        log.StoreId = storeId.Value;
        await _db.InvoiceIntegrationLogs.AddAsync(log, ct);
    }

    public async Task<List<InvoiceIntegrationLog>> GetLatestByInvoiceHeadAsync(
        int invoiceHeadId,
        int take = 20,
        CancellationToken ct = default)
    {
        take = LogReadLimits.NormalizePageSize(take);

        var logs = await _db.InvoiceIntegrationLogs
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.InvoiceHeadId == invoiceHeadId)
            .OrderByDescending(x => x.StartedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(ct);
        return SanitizeReads(logs);
    }

    public async Task<(List<InvoiceIntegrationLog> Items, int Total)> QueryAsync(
        InvoiceIntegrationLogQueryDto query,
        CancellationToken ct = default)
    {
        var offset = query.NormalizeAndValidate();
        var pageSize = query.PageSize;

        var q = _db.InvoiceIntegrationLogs
            .Include(x => x.InvoiceHead)
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .AsQueryable();

        if (query.FromDate.HasValue)
        {
            var fromUtc = VietnamTime.ToUtc(query.FromDate.Value.Date);
            q = q.Where(x => x.StartedAtUtc >= fromUtc);
        }

        if (query.ToDate.HasValue)
        {
            var toExclusiveUtc = VietnamTime.ToUtc(query.ToDate.Value.Date.AddDays(1));
            q = q.Where(x => x.StartedAtUtc < toExclusiveUtc);
        }

        if (query.InvoiceHeadId.HasValue && query.InvoiceHeadId.Value > 0)
        {
            q = q.Where(x => x.InvoiceHeadId == query.InvoiceHeadId.Value);
        }

        if (query.OrderId.HasValue && query.OrderId.Value > 0)
        {
            q = q.Where(x =>
                x.InvoiceHead != null &&
                x.InvoiceHead.OrderId == query.OrderId.Value);
        }

        if (query.ActionType.HasValue)
        {
            q = q.Where(x => x.ActionType == query.ActionType.Value);
        }

        if (query.IsSuccess.HasValue)
        {
            q = q.Where(x => x.IsSuccess == query.IsSuccess.Value);
        }

        var keyword = query.Keyword?.Trim();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            // Searching raw URL/error text would be an oracle for credentials
            // hidden by the display sanitizer. Search only visible identifiers
            // and exact codes admitted by the same read policy.
            var safeCode = InvoiceLogReadSanitizer.Code(keyword);
            if (safeCode == InvoiceLogReadSanitizer.Redacted) safeCode = null;
            q = q.Where(x =>
                (safeCode != null && x.ErrorCode == safeCode) ||
                (x.InvoiceHead != null &&
                    (
                        (x.InvoiceHead.InvoiceNumber != null && x.InvoiceHead.InvoiceNumber.Contains(keyword)) ||
                        (x.InvoiceHead.ProviderInvoiceNo != null && x.InvoiceHead.ProviderInvoiceNo.Contains(keyword)) ||
                        (x.InvoiceHead.TransactionUuid != null && x.InvoiceHead.TransactionUuid.Contains(keyword))
                    )
                ));
        }

        var total = await q.CountAsync(ct);
        if (offset >= total)
            return ([], total);

        var items = await q
            .OrderByDescending(x => x.StartedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync(ct);

        return (SanitizeReads(items), total);
    }

    public async Task<InvoiceIntegrationLog?> GetByIdAsync(
        int id,
        CancellationToken ct = default)
    {
        var log = await _db.InvoiceIntegrationLogs
            .Include(x => x.InvoiceHead)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
        return log is null ? null : SanitizeRead(log);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        var pendingLogs = _db.ChangeTracker.Entries<InvoiceIntegrationLog>()
            .Where(x => x.State == EntityState.Added).ToArray();
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // The caller reports log failure while preserving the provider result.
            // A failed optional insert must not poison later business/heartbeat saves.
            foreach (var entry in pendingLogs)
                if (entry.State == EntityState.Added)
                    entry.State = EntityState.Detached;
            throw;
        }
    }
    public async Task<List<InvoiceIntegrationLog>> GetLogsForDashboardAsync(
    DateTime fromDate,
    DateTime toDate,
    CancellationToken ct = default)
    {
        var from = VietnamTime.ToUtc(fromDate.Date);
        var toExclusive = VietnamTime.ToUtc(toDate.Date.AddDays(1));

        var logs = await _db.InvoiceIntegrationLogs
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.StartedAtUtc >= from &&
                x.StartedAtUtc < toExclusive)
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(500)
            .ToListAsync(ct);
        return SanitizeReads(logs);
    }

    // Every caller (including payload/dashboard readers) receives safe copies
    // materialized with AsNoTracking. Never rewrite historical evidence in SQL.
    private static List<InvoiceIntegrationLog> SanitizeReads(List<InvoiceIntegrationLog> logs)
    {
        foreach (var log in logs) SanitizeRead(log);
        return logs;
    }

    private static InvoiceIntegrationLog SanitizeRead(InvoiceIntegrationLog log)
    {
        log.RequestBody = InvoiceLogReadSanitizer.Body(log.RequestBody);
        log.ResponseBody = InvoiceLogReadSanitizer.Body(log.ResponseBody);
        log.RequestUrl = InvoiceLogReadSanitizer.Url(log.RequestUrl);
        log.ErrorCode = InvoiceLogReadSanitizer.Code(log.ErrorCode);
        log.ErrorMessage = InvoiceLogReadSanitizer.Error(log.ErrorMessage);
        return log;
    }
    public async Task<InvoiceIntegrationLogCleanupResultDto> CleanupAsync(
    InvoiceIntegrationLogCleanupRequestDto request,
    DateTime nowUtc,
    CancellationToken ct = default)
    {
        request.MaxRowsPerRun = request.MaxRowsPerRun <= 0
            ? 5000
            : Math.Min(request.MaxRowsPerRun, 50000);

        var result = new InvoiceIntegrationLogCleanupResultDto
        {
            DryRun = request.DryRun,
            NowUtc = nowUtc,
            MaxRowsPerRun = request.MaxRowsPerRun
        };

        var policies = BuildCleanupPolicies(request, nowUtc);

        var remainingDeleteLimit = request.MaxRowsPerRun;

        foreach (var policy in policies)
        {
            var query = BuildPolicyQuery(policy);

            var count = await query.CountAsync(ct);

            var row = new InvoiceIntegrationLogCleanupPolicyRowDto
            {
                PolicyName = policy.PolicyName,
                ActionNames = string.Join(", ", policy.ActionTypes.Select(x => x.ToString())),
                IsSuccess = policy.IsSuccess,
                RetentionDays = policy.RetentionDays,
                DeleteBeforeUtc = policy.DeleteBeforeUtc,
                CandidateCount = count,
                DeletedCount = 0
            };

            result.TotalCandidates += count;

            if (!request.DryRun && count > 0 && remainingDeleteLimit > 0)
            {
                var take = Math.Min(count, remainingDeleteLimit);

                var logs = await query
                    .OrderBy(x => x.StartedAtUtc)
                    .ThenBy(x => x.Id)
                    .Take(take)
                    .ToListAsync(ct);

                foreach (var log in logs)
                {
                    log.IsDeleted = true;
                }

                await _db.SaveChangesAsync(ct);

                row.DeletedCount = logs.Count;
                result.DeletedCount += logs.Count;
                remainingDeleteLimit -= logs.Count;
            }

            result.Rows.Add(row);

            if (!request.DryRun && remainingDeleteLimit <= 0)
                break;
        }

        if (request.DryRun)
        {
            result.Messages.Add("Đây là chế độ xem trước. Chưa xóa log nào.");
        }
        else
        {
            result.Messages.Add($"Đã dọn {result.DeletedCount:N0} log.");
        }

        result.Messages.Add("Các log quan trọng IssueInvoice, SearchByTransactionUuid, CancelInvoice được giữ lại dài hạn.");
        result.Messages.Add("Mặc định không xóa log lỗi, trừ khi bật IncludeFailedLogs.");

        return result;

        IQueryable<InvoiceIntegrationLog> BuildPolicyQuery(CleanupPolicy policy)
        {
            var q = _db.InvoiceIntegrationLogs
                .Where(x =>
                    !x.IsDeleted &&
                    x.IsSuccess == policy.IsSuccess &&
                    x.StartedAtUtc < policy.DeleteBeforeUtc);

            if (policy.ActionTypes.Any())
            {
                q = q.Where(x => policy.ActionTypes.Contains(x.ActionType));
            }

            if (policy.ExcludeCriticalActions)
            {
                var criticalActions = GetCriticalActions();
                q = q.Where(x => !criticalActions.Contains(x.ActionType));
            }

            return q;
        }
    }

    private static List<CleanupPolicy> BuildCleanupPolicies(
        InvoiceIntegrationLogCleanupRequestDto request,
        DateTime nowUtc)
    {
        var policies = new List<CleanupPolicy>();

        AddSuccessPolicy(
            policies,
            "Build JSON thành công",
            request.BuildPayloadSuccessDays,
            nowUtc,
            InvoiceIntegrationActionType.BuildPayload);

        AddSuccessPolicy(
            policies,
            "Preview PDF nháp thành công",
            request.PreviewDraftSuccessDays,
            nowUtc,
            InvoiceIntegrationActionType.PreviewDraft);

        AddSuccessPolicy(
            policies,
            "Gửi email thành công",
            request.SendEmailSuccessDays,
            nowUtc,
            InvoiceIntegrationActionType.SendEmail);

        AddSuccessPolicy(
            policies,
            "Đồng bộ danh sách thành công",
            request.SyncInvoiceListSuccessDays,
            nowUtc,
            InvoiceIntegrationActionType.SyncInvoiceList);

        AddSuccessPolicy(
            policies,
            "Tải file PDF/XML thành công",
            request.DownloadFileSuccessDays,
            nowUtc,
            InvoiceIntegrationActionType.DownloadPdf,
            InvoiceIntegrationActionType.DownloadZip);

        AddSuccessPolicy(
            policies,
            "Log phụ thành công",
            request.OtherSuccessDays,
            nowUtc,
            InvoiceIntegrationActionType.Login,
            InvoiceIntegrationActionType.CreateDraft,
            InvoiceIntegrationActionType.UpdatePaymentStatus,
            InvoiceIntegrationActionType.CancelPaymentStatus,
            InvoiceIntegrationActionType.UpdatePrintStatus);

        if (request.IncludeFailedLogs)
        {
            policies.Add(new CleanupPolicy
            {
                PolicyName = "Log lỗi cũ không quan trọng",
                IsSuccess = false,
                RetentionDays = NormalizeDays(request.FailedLogDays, 365),
                DeleteBeforeUtc = nowUtc.AddDays(-NormalizeDays(request.FailedLogDays, 365)),
                ActionTypes = new List<InvoiceIntegrationActionType>(),
                ExcludeCriticalActions = true
            });
        }

        return policies;
    }

    private static void AddSuccessPolicy(
        List<CleanupPolicy> policies,
        string policyName,
        int retentionDays,
        DateTime nowUtc,
        params InvoiceIntegrationActionType[] actionTypes)
    {
        var days = NormalizeDays(retentionDays, 30);

        policies.Add(new CleanupPolicy
        {
            PolicyName = policyName,
            IsSuccess = true,
            RetentionDays = days,
            DeleteBeforeUtc = nowUtc.AddDays(-days),
            ActionTypes = actionTypes.ToList(),
            ExcludeCriticalActions = false
        });
    }

    private static int NormalizeDays(int value, int defaultValue)
    {
        if (value <= 0)
            return defaultValue;

        return Math.Min(value, 3650);
    }

    private static List<InvoiceIntegrationActionType> GetCriticalActions()
    {
        return new List<InvoiceIntegrationActionType>
    {
        InvoiceIntegrationActionType.IssueInvoice,
        InvoiceIntegrationActionType.SearchByTransactionUuid,
        InvoiceIntegrationActionType.CancelInvoice
    };
    }

    private class CleanupPolicy
    {
        public string PolicyName { get; set; } = string.Empty;

        public bool IsSuccess { get; set; }

        public int RetentionDays { get; set; }

        public DateTime DeleteBeforeUtc { get; set; }

        public List<InvoiceIntegrationActionType> ActionTypes { get; set; } = new();

        public bool ExcludeCriticalActions { get; set; }
    }
}
