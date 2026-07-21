using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

/// <summary>
/// Phân bổ return qty vào các source valuation fragment còn outstanding.
///
/// Rule:
/// - duyệt theo thứ tự source fragment gốc
/// - fragment nào còn outstanding thì reverse trước
/// - không chia theo tỷ lệ
/// - không làm tròn ratio
/// - không gộp nhiều fragment thành một allocation
/// </summary>
public interface IReturnCostAllocator
{
    IReadOnlyList<ReturnCostAllocationDto> Allocate(
        IReadOnlyList<ReturnableValuationFragmentDto> fragments,
        decimal returnQtyBase);
}