using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

// Optional cost-specific capability, kept beside its implementation so the
// existing quantity-fragment interface and all external DTO shapes stay intact.
public interface IReturnableValuationCostEvidence
{
    Task<List<ReturnableValuationFragmentDto>> GetQuantityOnlyForOrderLineAsync(
        int orderId, int orderLineId, CancellationToken ct);
    Task ValidateVoidClosureAsync(int orderId, int orderLineId, CancellationToken ct);
}

/// <summary>
/// Dựng danh sách source valuation fragments còn outstanding cho 1 order line.
///
/// Ý tưởng:
/// - lấy toàn bộ outbound source entries của order line
/// - với mỗi source entry:
///     + lấy toàn bộ entries đang trỏ về source đó
///     + tách phần reverse quantity thực sự
///     + tách phần revaluation của source
///     + tính remaining qty
///     + nếu source đã được revaluation thì resolve final cost
///
/// Giai đoạn đầu ưu tiên:
/// - code rõ ràng
/// - dễ debug
/// - đúng logic trước
///
/// Tối ưu query có thể làm sau.
/// </summary>
public class ReturnableValuationFragmentService : IReturnableValuationFragmentService,
    IReturnableValuationCostEvidence
{
    private readonly IInventoryValuationEntryRepository _valuationRepository;
    private readonly IOrderLegalEntityAllocationReversalRepository _reversalRepository;

    public ReturnableValuationFragmentService(
        IInventoryValuationEntryRepository valuationRepository,
        IOrderLegalEntityAllocationReversalRepository reversalRepository)
    {
        _valuationRepository = valuationRepository;
        _reversalRepository = reversalRepository;
    }

    public Task<List<ReturnableValuationFragmentDto>> GetForOrderLineAsync(
      int orderId,
      int orderLineId,
      CancellationToken ct = default)
        => GetCoreAsync(orderId, orderLineId, true, ct);

    public Task<List<ReturnableValuationFragmentDto>> GetQuantityOnlyForOrderLineAsync(
        int orderId, int orderLineId, CancellationToken ct)
        => GetCoreAsync(orderId, orderLineId, false, ct);

    public async Task ValidateVoidClosureAsync(int orderId, int orderLineId, CancellationToken ct)
    {
        var sources = await _valuationRepository.GetSaleIssueEntriesByOrderLineAsync(orderId, orderLineId, ct);
        if (sources.Count == 0)
            throw new InvalidOperationException("Void is missing original sale cost evidence.");
        foreach (var source in sources)
        {
            var cost = SaleValuationCostPolicy.Evaluate(source,
                await _valuationRepository.GetRevaluationEntriesBySourceIdAsync(source.Id, ct),
                await _valuationRepository.GetReverseEntriesBySourceEntryIdAsync(source.Id, ct));
            if (cost.State != SaleValuationCostPolicy.Quality.Finalized || cost.Cost != 0 ||
                cost.InventoryReversedQuantity != -source.Quantity)
                throw new InvalidOperationException($"Void leaves unsupported/residual sale cost: {cost.Reason}");
        }
    }

    private async Task<List<ReturnableValuationFragmentDto>> GetCoreAsync(
        int orderId, int orderLineId, bool requireFinalCost, CancellationToken ct)
    {
        // Lấy toàn bộ outbound source entries của dòng bán gốc.
        // Nếu outbound đã split theo FIFO layer thì ở đây sẽ có nhiều source entry.
        var sourceEntries = await _valuationRepository.GetSaleIssueEntriesByOrderLineAsync(
            orderId,
            orderLineId,
            ct);

        var reversedBySource = await _reversalRepository
            .GetReversedBaseQuantityBySourceEntryIdsAsync(
                sourceEntries.Select(x => x.Id).ToList(),
                ct);

        var result = new List<ReturnableValuationFragmentDto>();

        foreach (var source in sourceEntries
                     .OrderBy(x => x.OccurredAtUtc)
                     .ThenBy(x => x.Id))
        {
            // Lấy các entry reverse quantity đang trỏ về source này.
            // Dùng cho việc tính source đã bị return/void bao nhiêu.
            var reverseEntries = await _valuationRepository.GetReverseEntriesBySourceEntryIdAsync(source.Id, ct);

            // Lấy riêng revaluation entries của source này.
            // Không phụ thuộc reverseEntries để tránh bỏ sót revaluation.
            var revaluationEntries = await _valuationRepository.GetRevaluationEntriesBySourceIdAsync(source.Id, ct);

            // Source outbound thường là số âm, nên lấy trị tuyệt đối để tính dễ.
            var sourceQuantityAbs = Math.Abs(source.Quantity);
            if (sourceQuantityAbs <= 0)
                continue;

            // =====================================================
            // 1. TÍNH QTY ĐÃ REVERSE
            //
            // Chỉ cộng các entry có quantity thực sự.
            // Revaluation có quantity = 0 nên không được tính vào reversed qty.
            // =====================================================
            var reverseQuantityEntries = reverseEntries
                .Where(x => x.EntryType == InventoryValuationEntryType.Inbound && x.Quantity > 0)
                .ToList();

            if (source.StoreId <= 0 || source.IsDeleted || reverseEntries.Any(x =>
                    x.IsDeleted || x.StoreId != source.StoreId ||
                    x.WarehouseId != source.WarehouseId || x.ProductVariantId != source.ProductVariantId ||
                    (x.EntryType != InventoryValuationEntryType.Revaluation &&
                     (x.EntryType != InventoryValuationEntryType.Inbound || x.Quantity <= 0))))
                throw new InvalidOperationException("Invalid source reversal quantity evidence.");

            var valuationReversedQuantityAbs = reverseQuantityEntries.Sum(x => Math.Abs(x.Quantity));
            var recordedReversedQuantityAbs = reversedBySource.GetValueOrDefault(source.Id);

            // Restock có cả valuation reverse và reversal record cho cùng quantity,
            // vì vậy dùng Max thay vì cộng để không đếm đôi. NoRestock chỉ có record.
            var reversedQuantityAbs = Math.Max(
                valuationReversedQuantityAbs,
                recordedReversedQuantityAbs);
            var remainingQuantityAbs = sourceQuantityAbs - reversedQuantityAbs;

            if (remainingQuantityAbs < 0 || recordedReversedQuantityAbs < 0)
                throw new InvalidOperationException("Source valuation quantity is over-reversed.");

            // NoRestock needs eligibility only, and must not be blocked merely
            // because cost is provisional. Its historical unit value is never posted.
            var finalUnitCost = source.UnitCost;
            if (requireFinalCost)
            {
                var cost = SaleValuationCostPolicy.Evaluate(source, revaluationEntries, reverseEntries);
                finalUnitCost = cost.RequireFinalUnitCost();
            }

            // Nếu source đã reverse hết hoặc vượt rồi thì bỏ qua.
            if (remainingQuantityAbs <= 0)
                continue;

            result.Add(new ReturnableValuationFragmentDto
            {
                InventoryTransactionId = source.InventoryTransactionId,
                WarehouseId = source.WarehouseId,
                ProductVariantId = source.ProductVariantId,
                SourceValuationEntryId = source.Id,
                ReferenceSubKey = source.ReferenceSubKey,
                SourceQuantityAbs = sourceQuantityAbs,
                ReversedQuantityAbs = reversedQuantityAbs,
                RemainingQuantityAbs = remainingQuantityAbs,

                // QUAN TRỌNG:
                // dùng FINAL COST nếu source đã được revaluation
                UnitCost = finalUnitCost,

                // The ledger retains historical provisional provenance; this DTO
                // describes the validated cost used by the current operation.
                IsProvisional = !requireFinalCost && source.IsProvisional,
                OccurredAtUtc = source.OccurredAtUtc
            });
        }

        return result;
    }

}
