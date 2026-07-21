using GaoApp.Application.DTOs.Orders.LegalEntityAllocation;

namespace GaoApp.Application.Interfaces.Services.Orders;

public interface IOrderLegalEntityAllocationService
{
    /// <summary>
    /// Tính thử allocation theo SalePriority trên snapshot đầu vào.
    /// Không tạo movement, không reserve, không lưu allocation và không sửa balance.
    /// </summary>
    OrderLegalEntityAllocationResult Preview(OrderLegalEntityAllocationRequest request);
}
