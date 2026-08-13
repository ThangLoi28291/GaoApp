using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.Services.Purchases;

public static class PurchaseReceiptConfirmPrerequisitePolicy
{
    public static void EnsureSupplierSelected(int? supplierId)
    {
        if (supplierId is null or <= 0)
        {
            throw new BusinessRuleException(
                "Vui lòng chọn nhà cung cấp trước khi duyệt nhập kho.");
        }
    }
}
