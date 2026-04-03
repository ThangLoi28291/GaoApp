using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;

namespace GaoApp.Application.Services.Inventory;

/// <summary>
/// Allocator chuẩn cho return mức 2.
///
/// Quy tắc đang chốt cho GaoApp/GaoMart:
/// - Return phải reverse theo THỨ TỰ NGƯỢC với lúc outbound đã consume.
/// - Nghĩa là fragment outbound phát sinh SAU sẽ được reverse TRƯỚC.
/// - Không ratio.
/// - Không gộp cost giữa các fragment.
/// - Giữ nguyên unit cost + provisional flag của source fragment.
///
/// Ví dụ:
/// - Bán: 5 @ 15000, rồi 3 @ 13000
/// - Return 4
/// => reverse đúng phải là:
///    3 @ 13000 + 1 @ 15000
///
/// Nếu đi xuôi từ fragment cũ nhất:
/// => sẽ thành 4 @ 15000
/// => sai với kỳ vọng reverse source fragments của sales return mức 2.
/// </summary>
public class ReturnCostAllocator : IReturnCostAllocator
{
    public IReadOnlyList<ReturnCostAllocationDto> Allocate(
        IReadOnlyList<ReturnableValuationFragmentDto> fragments,
        decimal returnQtyBase)
    {
        if (fragments == null)
            throw new ArgumentNullException(nameof(fragments));

        if (returnQtyBase <= 0)
            throw new InvalidOperationException("Return quantity base phải > 0.");

        // =====================================================
        // QUAN TRỌNG NHẤT:
        // Reverse theo thứ tự NGƯỢC của outbound fragments.
        //
        // Outbound bán ra thường được tạo theo thứ tự consume FIFO:
        //   fragment 1 -> fragment 2 -> fragment 3
        //
        // Khi return, để mirror ngược lại đúng logic,
        // phải reverse từ fragment cuối cùng trở về trước:
        //   fragment 3 -> fragment 2 -> fragment 1
        // =====================================================
        var orderedFragments = fragments
            .Where(x => x != null)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.SourceValuationEntryId)
            .ToList();

        if (orderedFragments.Count == 0)
            throw new InvalidOperationException("Không có source fragment để allocate return cost.");

        var remainingToAllocate = returnQtyBase;
        var result = new List<ReturnCostAllocationDto>();

        foreach (var fragment in orderedFragments)
        {
            if (remainingToAllocate <= 0)
                break;

            if (fragment.RemainingQuantityAbs <= 0)
                continue;

            var qty = Math.Min(fragment.RemainingQuantityAbs, remainingToAllocate);
            if (qty <= 0)
                continue;

            result.Add(new ReturnCostAllocationDto
            {
                SourceValuationEntryId = fragment.SourceValuationEntryId,
                SourceReferenceSubKey = fragment.ReferenceSubKey,
                Quantity = qty,
                UnitCost = fragment.UnitCost,
                IsProvisional = fragment.IsProvisional
            });

            remainingToAllocate -= qty;
        }

        if (remainingToAllocate > 0)
        {
            throw new InvalidOperationException(
                $"Không đủ source fragment outstanding để reverse. Thiếu: {remainingToAllocate:0.####}");
        }

        return result;
    }
}