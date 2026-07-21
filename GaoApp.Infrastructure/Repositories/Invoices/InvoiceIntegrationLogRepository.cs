using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
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
        await _db.InvoiceIntegrationLogs.AddAsync(log, ct);
    }

    public async Task<List<InvoiceIntegrationLog>> GetLatestByInvoiceHeadAsync(
        int invoiceHeadId,
        int take = 20,
        CancellationToken ct = default)
    {
        if (take <= 0)
            take = 20;

        return await _db.InvoiceIntegrationLogs
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.InvoiceHeadId == invoiceHeadId)
            .OrderByDescending(x => x.StartedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<(List<InvoiceIntegrationLog> Items, int Total)> QueryAsync(
        InvoiceIntegrationLogQueryDto query,
        CancellationToken ct = default)
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        var q = _db.InvoiceIntegrationLogs
            .Include(x => x.InvoiceHead)
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .AsQueryable();

        if (query.FromDate.HasValue)
        {
            var fromUtc = query.FromDate.Value.Date;
            q = q.Where(x => x.StartedAtUtc >= fromUtc);
        }

        if (query.ToDate.HasValue)
        {
            var toExclusiveUtc = query.ToDate.Value.Date.AddDays(1);
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
            q = q.Where(x =>
                (x.RequestUrl != null && x.RequestUrl.Contains(keyword)) ||
                (x.ErrorCode != null && x.ErrorCode.Contains(keyword)) ||
                (x.ErrorMessage != null && x.ErrorMessage.Contains(keyword)) ||
                (x.InvoiceHead != null &&
                    (
                        (x.InvoiceHead.InvoiceNumber != null && x.InvoiceHead.InvoiceNumber.Contains(keyword)) ||
                        (x.InvoiceHead.ProviderInvoiceNo != null && x.InvoiceHead.ProviderInvoiceNo.Contains(keyword)) ||
                        (x.InvoiceHead.TransactionUuid != null && x.InvoiceHead.TransactionUuid.Contains(keyword))
                    )
                ));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(x => x.StartedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<InvoiceIntegrationLog?> GetByIdAsync(
        int id,
        CancellationToken ct = default)
    {
        return _db.InvoiceIntegrationLogs
            .Include(x => x.InvoiceHead)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
    public async Task<List<InvoiceIntegrationLog>> GetLogsForDashboardAsync(
    DateTime fromDate,
    DateTime toDate,
    CancellationToken ct = default)
    {
        var from = fromDate.Date;
        var toExclusive = toDate.Date.AddDays(1);

        return await _db.InvoiceIntegrationLogs
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.StartedAtUtc >= from &&
                x.StartedAtUtc < toExclusive)
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(500)
            .ToListAsync(ct);
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