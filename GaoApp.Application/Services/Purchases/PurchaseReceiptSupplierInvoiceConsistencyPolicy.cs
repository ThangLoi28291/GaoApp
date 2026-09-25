using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.Services.Purchases;

public static class PurchaseReceiptSupplierInvoiceConsistencyPolicy
{
    public static void EnsureCanConfirm(
        int? receiptSupplierId,
        IReadOnlyCollection<int?> linkedCanonicalSupplierIds)
    {
        if (linkedCanonicalSupplierIds.Count == 0)
            return;
        if (!receiptSupplierId.HasValue || receiptSupplierId.Value <= 0)
            throw new BusinessRuleException(
                "Phiếu nhập phải có nhà cung cấp trước khi xác nhận hóa đơn liên kết.");
        if (linkedCanonicalSupplierIds.Any(x =>
                !x.HasValue || x.Value != receiptSupplierId.Value))
            throw new BusinessRuleException(
                "Nhà cung cấp chuẩn của hóa đơn liên kết không khớp nhà cung cấp phiếu nhập.");
    }
}
