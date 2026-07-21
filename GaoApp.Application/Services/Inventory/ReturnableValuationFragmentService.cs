using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

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
public class ReturnableValuationFragmentService : IReturnableValuationFragmentService
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

    public async Task<List<ReturnableValuationFragmentDto>> GetForOrderLineAsync(
      int orderId,
      int orderLineId,
      CancellationToken ct = default)
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
                .Where(x => x.Quantity != 0)
                .ToList();

            var valuationReversedQuantityAbs = reverseQuantityEntries.Sum(x => Math.Abs(x.Quantity));
            var recordedReversedQuantityAbs = reversedBySource.GetValueOrDefault(source.Id);

            // Restock có cả valuation reverse và reversal record cho cùng quantity,
            // vì vậy dùng Max thay vì cộng để không đếm đôi. NoRestock chỉ có record.
            var reversedQuantityAbs = Math.Max(
                valuationReversedQuantityAbs,
                recordedReversedQuantityAbs);
            var remainingQuantityAbs = sourceQuantityAbs - reversedQuantityAbs;

            // Nếu source đã reverse hết hoặc vượt rồi thì bỏ qua.
            if (remainingQuantityAbs <= 0)
                continue;

            // =====================================================
            // 2. RESOLVE FINAL COST CHO SOURCE
            //
            // Nếu source có revaluation thì return phải dùng FINAL COST,
            // không dùng provisional cost ban đầu nữa.
            //
            // Rule:
            // finalUnitCost = source.UnitCost + (totalRevaluationAmount / sourceQuantityAbs)
            //
            // Ví dụ:
            // source: -3 @ 15000
            // revaluation: Amount = -9000
            // => final cost = 15000 + (-9000 / 3) = 12000
            // =====================================================
            var totalRevaluationAmount = revaluationEntries.Sum(x => x.Amount);

            var finalUnitCost = source.UnitCost;
            if (totalRevaluationAmount != 0)
            {
                finalUnitCost = source.UnitCost + (totalRevaluationAmount / sourceQuantityAbs);
            }

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

                // Giữ trace lịch sử source vốn từng provisional hay không.
                IsProvisional = source.IsProvisional,
                OccurredAtUtc = source.OccurredAtUtc
            });
        }

        return result;
    }

}
