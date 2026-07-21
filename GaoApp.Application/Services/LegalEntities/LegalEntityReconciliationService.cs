using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.LegalEntities;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Application.Interfaces.Services.LegalEntities;

namespace GaoApp.Application.Services.LegalEntities;

public sealed class LegalEntityReconciliationService : ILegalEntityReconciliationService
{
    private readonly ILegalEntityReconciliationRepository _repository;
    private readonly ICurrentStore _currentStore;

    public LegalEntityReconciliationService(
        ILegalEntityReconciliationRepository repository,
        ICurrentStore currentStore)
    {
        _repository = repository;
        _currentStore = currentStore;
    }

    public Task<LegalEntityOrderBreakdownDto?> GetOrderBreakdownAsync(
        int orderId,
        CancellationToken ct = default)
    {
        if (orderId <= 0)
            throw new ArgumentOutOfRangeException(nameof(orderId));

        return _repository.GetOrderBreakdownAsync(_currentStore.StoreId, orderId, ct);
    }

    public Task<LegalEntityReconciliationReportDto> GetReportAsync(
        LegalEntityReconciliationQueryDto query,
        CancellationToken ct = default)
        => GetAsync(query, exportAll: false, ct);

    public Task<LegalEntityReconciliationReportDto> GetExportAsync(
        LegalEntityReconciliationQueryDto query,
        CancellationToken ct = default)
        => GetAsync(query, exportAll: true, ct);

    private Task<LegalEntityReconciliationReportDto> GetAsync(
        LegalEntityReconciliationQueryDto query,
        bool exportAll,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var today = DateTime.Today;
        var toDate = query.ToDate == default ? today : query.ToDate.Date;
        var fromDate = query.FromDate == default ? toDate.AddDays(-29) : query.FromDate.Date;

        if (fromDate > toDate)
            (fromDate, toDate) = (toDate, fromDate);

        query.FromDate = fromDate;
        query.ToDate = toDate;
        query.Keyword = string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword.Trim();
        query.Page = exportAll ? 1 : Math.Max(1, query.Page);
        query.PageSize = exportAll ? int.MaxValue : Math.Clamp(query.PageSize, 10, 100);

        var fromUtc = DateTime.SpecifyKind(fromDate, DateTimeKind.Local).ToUniversalTime();
        var toUtcExclusive = DateTime.SpecifyKind(toDate.AddDays(1), DateTimeKind.Local).ToUniversalTime();

        return _repository.QueryAsync(
            _currentStore.StoreId,
            query,
            fromUtc,
            toUtcExclusive,
            ct);
    }
}
