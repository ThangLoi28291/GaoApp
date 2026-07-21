using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Services.Orders;

public interface IOrderLegalEntityReversalService
{
    /// <summary>
    /// Đảo toàn bộ tồn của order theo allocation gốc. Trả false nếu order legacy.
    /// Caller vẫn chịu trách nhiệm đảo payment/shift/reward và commit transaction.
    /// </summary>
    Task<bool> ReverseVoidIfAllocatedAsync(
        Order order,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Phân bổ một dòng return về đúng fragment LegalEntity gốc, có hoặc không restock.
    /// Trả false nếu order legacy để caller dùng luồng cũ.
    /// </summary>
    Task<bool> ReverseSalesReturnLineIfAllocatedAsync(
        Order order,
        SalesReturn salesReturn,
        SalesReturnLine salesReturnLine,
        CancellationToken ct = default);
}
