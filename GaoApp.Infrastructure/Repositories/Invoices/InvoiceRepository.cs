using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Invoices;

public class InvoiceRepository : IInvoiceRepository
{
    private readonly AppDbContext _db;

    public InvoiceRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<Order?> GetOrderForInvoiceAsync(
        int orderId,
        CancellationToken ct = default)
    {
        return _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);
    }

    public Task<Order?> GetOrderWithLinesForInvoiceAsync(
        int orderId,
        CancellationToken ct = default)
    {
        return _db.Orders
            .Include(x => x.Lines)
                .ThenInclude(x => x.Variant)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);
    }

    public Task<InvoiceHead?> GetInvoiceHeadByOrderIdAsync(
        int orderId,
        CancellationToken ct = default)
    {
        return _db.InvoiceHeads
            .Include(x => x.LegalEntity)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
    }

    public Task<InvoiceHead?> GetInvoiceHeadWithDetailsByOrderIdAsync(
       int orderId,
       CancellationToken ct = default)
    {
        return _db.InvoiceHeads
            .Include(x => x.LegalEntity)
            .Include(x => x.Details)
            .FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
    }

    public Task<List<InvoiceHead>> GetOriginalInvoiceHeadsWithDetailsByOrderIdAsync(
        int orderId,
        CancellationToken ct = default)
    {
        return _db.InvoiceHeads
            .Include(x => x.LegalEntity)
            .Include(x => x.Details)
                .ThenInclude(x => x.OrderLegalEntityAllocation)
            .Where(x => x.OrderId == orderId && x.OriginalInvoiceHeadId == null)
            .OrderBy(x => x.LegalEntityId == null ? 0 : 1)
            .ThenBy(x => x.LegalEntityId)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    public Task<InvoiceHead?> GetInvoiceHeadByIdAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        return _db.InvoiceHeads
            .Include(x => x.LegalEntity)
            .Include(x => x.Details)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == invoiceHeadId, ct);
    }

    public async Task AddInvoiceHeadAsync(
        InvoiceHead invoiceHead,
        CancellationToken ct = default)
    {
        await _db.InvoiceHeads.AddAsync(invoiceHead, ct);
    }

    public Task AddInvoiceHeadsAsync(
        IReadOnlyCollection<InvoiceHead> invoiceHeads,
        CancellationToken ct = default)
    {
        if (invoiceHeads == null || invoiceHeads.Count == 0)
            return Task.CompletedTask;

        return _db.InvoiceHeads.AddRangeAsync(invoiceHeads, ct);
    }

    public async Task AddInvoiceDetailsAsync(
        List<InvoiceDetail> details,
        CancellationToken ct = default)
    {
        await _db.InvoiceDetails.AddRangeAsync(details, ct);
    }
    public Task<InvoiceHead?> GetInvoiceHeadWithDetailsByIdAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        return _db.InvoiceHeads
            .Include(x => x.LegalEntity)
            .Include(x => x.Details)
            .FirstOrDefaultAsync(x => x.Id == invoiceHeadId, ct);
    }

    public async Task AddInvoiceDetailAsync(
        InvoiceDetail detail,
        CancellationToken ct = default)
    {
        await _db.InvoiceDetails.AddAsync(detail, ct);
    }
    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
    public async Task<(List<InvoiceHead> Items, int Total)> QueryInvoiceHeadsAsync(
        DateTime? fromDate,
        DateTime? toDate,
        int? orderId,
        string? keyword,
        InvoiceListDisplayMode displayMode,
        InvoiceListSortMode sortMode,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 20;

        var query = _db.InvoiceHeads
            .Include(x => x.Order)
            .Include(x => x.LegalEntity)
            .Include(x => x.Details)
            .AsNoTracking()
            .AsQueryable();

        if (fromDate.HasValue)
        {
            var from = fromDate.Value.Date;
            query = query.Where(x => x.InvoiceDate >= from);
        }

        if (toDate.HasValue)
        {
            var toExclusive = toDate.Value.Date.AddDays(1);
            query = query.Where(x => x.InvoiceDate < toExclusive);
        }

        if (orderId.HasValue && orderId.Value > 0)
        {
            query = query.Where(x => (x.OrderId == orderId.Value || x.LegacySourceId == orderId.Value));
        }

        keyword = keyword?.Trim();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(x =>
                (x.InvoiceNumber != null && x.InvoiceNumber.Contains(keyword)) ||
                (x.BuyerName != null && x.BuyerName.Contains(keyword)) ||
                (x.Order != null &&
                 x.Order!.OrderNumber != null &&
                 x.Order!.OrderNumber.Contains(keyword)));
        }

        // =========================================================
        // FILTER HIỂN THỊ
        // =========================================================
        switch (displayMode)
        {
            case InvoiceListDisplayMode.HasQuantity:
                query = query.Where(x =>
                    x.Details.Any(d =>
                        !d.IsDeleted &&
                        d.Quantity > 0));
                break;

            case InvoiceListDisplayMode.Empty:
                query = query.Where(x =>
                    !x.Details.Any(d =>
                        !d.IsDeleted &&
                        d.Quantity > 0));
                break;

            case InvoiceListDisplayMode.Locked:
                query = query.Where(x => x.IsLocked);
                break;

            case InvoiceListDisplayMode.Unlocked:
                query = query.Where(x => !x.IsLocked);
                break;

            case InvoiceListDisplayMode.HasAutoLines:
                query = query.Where(x =>
                    x.Details.Any(d =>
                        !d.IsDeleted &&
                        d.SourceType == InvoiceDetailSourceType.FromOrderLine));
                break;

            case InvoiceListDisplayMode.HasManualLines:
                query = query.Where(x =>
                    x.Details.Any(d =>
                        !d.IsDeleted &&
                        d.SourceType == InvoiceDetailSourceType.Manual));
                break;

            case InvoiceListDisplayMode.ManualWaitingInfo:
            case InvoiceListDisplayMode.ManualReady:
            case InvoiceListDisplayMode.ManualNeedsAttention:
                query = ApplyManualWorkspaceFilter(query, displayMode);
                break;

            case InvoiceListDisplayMode.All:
            default:
                break;
        }

        var total = await query.CountAsync(ct);

        // =========================================================
        // SORT DANH SÁCH
        // =========================================================
        // GHI CHÚ:
        // Phần này cho phép người dùng chọn nhiều kiểu sắp xếp.
        // Mặc định là HasLinesThenOrderIdDesc:
        // - Có sản phẩm lên trước.
        // - Sau đó OrderId mới nhất lên trước.
        IOrderedQueryable<InvoiceHead> orderedQuery = sortMode switch
        {
            InvoiceListSortMode.HasLinesThenOrderIdDesc => query
                .OrderByDescending(x =>
                    x.Details.Any(d =>
                        !d.IsDeleted &&
                        d.Quantity > 0))
                .ThenByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id),

            InvoiceListSortMode.OrderIdDesc => query
                .OrderByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id),

            InvoiceListSortMode.OrderIdAsc => query
                .OrderBy(x => x.OrderId)
                .ThenBy(x => x.Id),

            InvoiceListSortMode.InvoiceDateDesc => query
                .OrderByDescending(x => x.InvoiceDate)
                .ThenByDescending(x => x.Id),

            InvoiceListSortMode.InvoiceDateAsc => query
                .OrderBy(x => x.InvoiceDate)
                .ThenBy(x => x.Id),

            InvoiceListSortMode.GrandTotalDesc => query
                .OrderByDescending(x => x.GrandTotal)
                .ThenByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id),

            InvoiceListSortMode.GrandTotalAsc => query
                .OrderBy(x => x.GrandTotal)
                .ThenByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id),

            InvoiceListSortMode.QuantityDesc => query
                .OrderByDescending(x => x.TotalQuantity)
                .ThenByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id),

            InvoiceListSortMode.QuantityAsc => query
                .OrderBy(x => x.TotalQuantity)
                .ThenByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id),

            InvoiceListSortMode.AutoLineCountDesc => query
                .OrderByDescending(x => x.Details.Count(d =>
                    !d.IsDeleted &&
                    d.SourceType == InvoiceDetailSourceType.FromOrderLine))
                .ThenByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id),

            InvoiceListSortMode.ManualLineCountDesc => query
                .OrderByDescending(x => x.Details.Count(d =>
                    !d.IsDeleted &&
                    d.SourceType == InvoiceDetailSourceType.Manual))
                .ThenByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id),

            // GHI CHÚ:
            // Mặc định cuối cùng: OrderId mới nhất.
            _ => query
                .OrderByDescending(x => x.OrderId)
                .ThenByDescending(x => x.Id)
        };

        var items = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    private static IQueryable<InvoiceHead> ApplyManualWorkspaceFilter(
        IQueryable<InvoiceHead> query,
        InvoiceListDisplayMode displayMode)
    {
        // Keep the issued-like rules aligned with InvoiceIssuanceStatePolicy,
        // expressed inline so EF filters in SQL before counting and paging.
        query = query.Where(x =>
            x.Order != null &&
            x.Order.InvoiceIssuanceRoute == InvoiceIssuanceRoute.Manual &&
            string.IsNullOrWhiteSpace(x.ProviderInvoiceNo) &&
            x.IssuedAtUtc == null &&
            x.ProviderStatus != InvoiceProviderStatus.Issued &&
            x.ProviderStatus != InvoiceProviderStatus.PdfDownloaded &&
            x.ProviderStatus != InvoiceProviderStatus.ZipDownloaded &&
            x.ProviderStatus != InvoiceProviderStatus.EmailSent);

        if (displayMode == InvoiceListDisplayMode.ManualNeedsAttention)
        {
            return query.Where(x =>
                (x.ProviderStatus != InvoiceProviderStatus.LocalDraft &&
                 x.ProviderStatus != InvoiceProviderStatus.ReadyToIssue &&
                 x.ProviderStatus != InvoiceProviderStatus.Previewed) ||
                !string.IsNullOrWhiteSpace(x.LastErrorCode) ||
                !string.IsNullOrWhiteSpace(x.LastErrorMessage) ||
                x.GrandTotal <= 0 ||
                !x.Details.Any(d => !d.IsDeleted && d.Quantity > 0 && d.TotalAmount > 0));
        }

        query = query.Where(x =>
            (x.ProviderStatus == InvoiceProviderStatus.LocalDraft ||
             x.ProviderStatus == InvoiceProviderStatus.ReadyToIssue ||
             x.ProviderStatus == InvoiceProviderStatus.Previewed) &&
            string.IsNullOrWhiteSpace(x.LastErrorCode) &&
            string.IsNullOrWhiteSpace(x.LastErrorMessage) &&
            x.GrandTotal > 0 &&
            x.Details.Any(d => !d.IsDeleted && d.Quantity > 0 && d.TotalAmount > 0));

        var buyerInfoComplete = displayMode == InvoiceListDisplayMode.ManualReady;
        return query.Where(x =>
            (!string.IsNullOrWhiteSpace(x.BuyerAddress) &&
              ((x.BuyerType == InvoiceBuyerTypes.Individual &&
                !string.IsNullOrWhiteSpace(x.BuyerName)) ||
               (x.BuyerType == InvoiceBuyerTypes.Business &&
                !string.IsNullOrWhiteSpace(x.BuyerLegalName) &&
                !string.IsNullOrWhiteSpace(x.BuyerTaxCode)))) == buyerInfoComplete);
    }

    public Task<InvoiceHead?> GetInvoiceHeadDetailAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        return _db.InvoiceHeads
            .Include(x => x.Order)
            .Include(x => x.LegalEntity)
            .Include(x => x.Details)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == invoiceHeadId, ct);
    }
    public Task<InvoiceDetail?> GetInvoiceDetailByIdAsync(
    int invoiceDetailId,
    CancellationToken ct = default)
    {
        return _db.InvoiceDetails
            .FirstOrDefaultAsync(x => x.Id == invoiceDetailId, ct);
    }

    public void UpdateInvoiceHead(InvoiceHead invoiceHead)
    {
        _db.InvoiceHeads.Update(invoiceHead);
    }
    public async Task<List<InvoiceHead>> GetByTransactionUuidsAsync(
        List<string> transactionUuids,
        CancellationToken ct = default)
    {
        transactionUuids = transactionUuids
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!transactionUuids.Any())
            return new List<InvoiceHead>();

        return await _db.InvoiceHeads
            .Where(x =>
                !x.IsDeleted &&
                x.TransactionUuid != null &&
                transactionUuids.Contains(x.TransactionUuid))
            .ToListAsync(ct);
    }

    public async Task<List<InvoiceHead>> GetByProviderInvoiceNosAsync(
        List<string> providerInvoiceNos,
        CancellationToken ct = default)
    {
        providerInvoiceNos = providerInvoiceNos
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!providerInvoiceNos.Any())
            return new List<InvoiceHead>();

        return await _db.InvoiceHeads
            .Where(x =>
                !x.IsDeleted &&
                (
                    x.ProviderInvoiceNo != null && providerInvoiceNos.Contains(x.ProviderInvoiceNo) ||
                    x.InvoiceNumber != null && providerInvoiceNos.Contains(x.InvoiceNumber)
                ))
            .ToListAsync(ct);
    }

    public async Task<List<InvoiceHead>> GetInvoiceHeadsForDashboardAsync(
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct = default)
    {
        var from = fromDate.Date;
        var toExclusive = toDate.Date.AddDays(1);

        return await _db.InvoiceHeads
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.InvoiceDate >= from &&
                x.InvoiceDate < toExclusive)
            .ToListAsync(ct);
    }
    public async Task<InvoiceHead?> GetLatestBuyerInfoByTaxCodeAsync(
     int storeId,
     string taxCode,
     CancellationToken ct = default)
    {
        taxCode = (taxCode ?? string.Empty).Trim();

        if (storeId <= 0 || string.IsNullOrWhiteSpace(taxCode))
            return null;

        return await _db.InvoiceHeads
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.BuyerTaxCode != null &&
                x.BuyerTaxCode == taxCode)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }
    public async Task<(ViettelInvoiceDashboardSummaryDto Summary, List<InvoiceHead> Items, int Total)> QueryViettelDashboardAsync(
    ViettelInvoiceDashboardQueryDto query,
    CancellationToken ct = default)
    {
        query ??= new ViettelInvoiceDashboardQueryDto();

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        var baseQuery = _db.InvoiceHeads
            .Include(x => x.Order)
            .Include(x => x.LegalEntity)
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Where(x =>
                x.ProviderCode == null ||
                x.ProviderCode == "" ||
                x.ProviderCode == "VIETTEL")
            .AsQueryable();

        if (query.FromDate.HasValue)
        {
            var from = query.FromDate.Value.Date;
            baseQuery = baseQuery.Where(x => x.InvoiceDate >= from);
        }

        if (query.ToDate.HasValue)
        {
            var toExclusive = query.ToDate.Value.Date.AddDays(1);
            baseQuery = baseQuery.Where(x => x.InvoiceDate < toExclusive);
        }

        var keyword = query.Keyword?.Trim();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            baseQuery = baseQuery.Where(x =>
                (x.InvoiceNumber != null && x.InvoiceNumber.Contains(keyword)) ||
                (x.ProviderInvoiceNo != null && x.ProviderInvoiceNo.Contains(keyword)) ||
                (x.TransactionUuid != null && x.TransactionUuid.Contains(keyword)) ||
                (x.BuyerName != null && x.BuyerName.Contains(keyword)) ||
                (x.BuyerLegalName != null && x.BuyerLegalName.Contains(keyword)) ||
                (x.BuyerTaxCode != null && x.BuyerTaxCode.Contains(keyword)) ||
                (x.Order != null &&
                 x.Order!.OrderNumber != null &&
                 x.Order!.OrderNumber.Contains(keyword)));
        }

        var summary = new ViettelInvoiceDashboardSummaryDto
        {
            TotalInvoices = await baseQuery.CountAsync(ct),
            IssuedCount = await WhereIssuedLike(baseQuery).CountAsync(ct),
            MissingPdfCount = await WhereIssuedMissingPdf(baseQuery).CountAsync(ct),
            MissingZipXmlCount = await WhereIssuedMissingZipXml(baseQuery).CountAsync(ct),
            MissingEmailCount = await WhereIssuedMissingEmail(baseQuery).CountAsync(ct),
            PdfFailedCount = await baseQuery.CountAsync(x =>
                x.OfficialPdfStatus == InvoiceFileDownloadStatus.Failed, ct),
            ZipXmlFailedCount = await baseQuery.CountAsync(x =>
                x.OfficialZipXmlStatus == InvoiceFileDownloadStatus.Failed, ct),
            EmailFailedCount = await baseQuery.CountAsync(x =>
                x.EmailStatus == InvoiceEmailSendStatus.Failed, ct),
            UuidNeedSyncCount = await WhereUuidNeedSync(baseQuery).CountAsync(ct),
            IssueFailedCount = await baseQuery.CountAsync(x =>
                x.ProviderStatus == InvoiceProviderStatus.IssueFailed, ct),
            CompleteCount = await WhereIssuedComplete(baseQuery).CountAsync(ct)
        };

        var filteredQuery = ApplyDashboardFilter(
            baseQuery,
            query.Filter);

        var total = await filteredQuery.CountAsync(ct);

        var items = await filteredQuery
            .OrderByDescending(x => x.InvoiceDate)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (summary, items, total);
    }

    private static IQueryable<InvoiceHead> ApplyDashboardFilter(
        IQueryable<InvoiceHead> query,
        ViettelInvoiceDashboardFilter filter)
    {
        return filter switch
        {
            ViettelInvoiceDashboardFilter.IssuedMissingPdf =>
                WhereIssuedMissingPdf(query),

            ViettelInvoiceDashboardFilter.IssuedMissingZipXml =>
                WhereIssuedMissingZipXml(query),

            ViettelInvoiceDashboardFilter.IssuedMissingEmail =>
                WhereIssuedMissingEmail(query),

            ViettelInvoiceDashboardFilter.PdfFailed =>
                query.Where(x => x.OfficialPdfStatus == InvoiceFileDownloadStatus.Failed),

            ViettelInvoiceDashboardFilter.ZipXmlFailed =>
                query.Where(x => x.OfficialZipXmlStatus == InvoiceFileDownloadStatus.Failed),

            ViettelInvoiceDashboardFilter.EmailFailed =>
                query.Where(x => x.EmailStatus == InvoiceEmailSendStatus.Failed),

            ViettelInvoiceDashboardFilter.UuidNeedSync =>
                WhereUuidNeedSync(query),

            ViettelInvoiceDashboardFilter.IssueFailed =>
                query.Where(x => x.ProviderStatus == InvoiceProviderStatus.IssueFailed),

            ViettelInvoiceDashboardFilter.IssuedComplete =>
                WhereIssuedComplete(query),

            ViettelInvoiceDashboardFilter.All or _ =>
                query
        };
    }

    private static IQueryable<InvoiceHead> WhereIssuedLike(
        IQueryable<InvoiceHead> query)
    {
        var issuedStatuses = new[]
        {
        InvoiceProviderStatus.Issued,
        InvoiceProviderStatus.PdfDownloaded,
        InvoiceProviderStatus.ZipDownloaded,
        InvoiceProviderStatus.EmailSent
    };

        return query.Where(x =>
            (x.ProviderInvoiceNo != null && x.ProviderInvoiceNo != "") ||
            issuedStatuses.Contains(x.ProviderStatus));
    }

    private static IQueryable<InvoiceHead> WhereIssuedMissingPdf(
        IQueryable<InvoiceHead> query)
    {
        return WhereIssuedLike(query)
            .Where(x =>
                x.OfficialPdfStatus != InvoiceFileDownloadStatus.Downloaded &&
                (x.PdfFilePath == null || x.PdfFilePath == ""));
    }

    private static IQueryable<InvoiceHead> WhereIssuedMissingZipXml(
        IQueryable<InvoiceHead> query)
    {
        return WhereIssuedLike(query)
            .Where(x =>
                x.OfficialZipXmlStatus != InvoiceFileDownloadStatus.Downloaded &&
                (x.ZipFilePath == null || x.ZipFilePath == ""));
    }

    private static IQueryable<InvoiceHead> WhereIssuedMissingEmail(
        IQueryable<InvoiceHead> query)
    {
        return WhereIssuedLike(query)
            .Where(x => x.EmailStatus != InvoiceEmailSendStatus.Sent);
    }

    private static IQueryable<InvoiceHead> WhereIssuedComplete(
        IQueryable<InvoiceHead> query)
    {
        return WhereIssuedLike(query)
            .Where(x =>
                (
                    x.OfficialPdfStatus == InvoiceFileDownloadStatus.Downloaded ||
                    (x.PdfFilePath != null && x.PdfFilePath != "")
                ) &&
                (
                    x.OfficialZipXmlStatus == InvoiceFileDownloadStatus.Downloaded ||
                    (x.ZipFilePath != null && x.ZipFilePath != "")
                ) &&
                x.EmailStatus == InvoiceEmailSendStatus.Sent);
    }

    private static IQueryable<InvoiceHead> WhereUuidNeedSync(
        IQueryable<InvoiceHead> query)
    {
        return query.Where(x =>
            x.ProviderStatus == InvoiceProviderStatus.Issuing ||
            x.ProviderStatus == InvoiceProviderStatus.IssuedWaitingNumber ||
            (
                x.ProviderStatus == InvoiceProviderStatus.IssueFailed &&
                (
                    x.LastErrorCode == "TIMEOUT" ||
                    x.LastErrorCode == "HTTP_500" ||
                    x.LastErrorCode == "VIETTEL_SERVER_500" ||
                    (x.LastErrorCode != null && x.LastErrorCode.StartsWith("HTTP_5")) ||
                    (x.LastErrorMessage != null && x.LastErrorMessage.Contains("timeout")) ||
                    (x.LastErrorMessage != null && x.LastErrorMessage.Contains("HTTP 500"))
                )
            ));
    }
}
