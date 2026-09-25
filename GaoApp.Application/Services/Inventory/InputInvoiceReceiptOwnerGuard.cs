using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public sealed class InputInvoiceReceiptOwnerGuard(
    IWarehouseRepository warehouses,
    IInputInvoiceBuyerOwnerResolutionService resolver)
    : IInputInvoiceReceiptOwnerGuard
{
    public async Task<InputInvoiceReceiptOwnerDecision> ValidateLinkWithinTransactionAsync(
        int storeId,
        StockDocument receipt,
        InputInvoiceHead invoice,
        CancellationToken ct = default)
    {
        EnsureReceipt(storeId, receipt);
        var confirmed = receipt.Status == StockDocumentStatus.Confirmed;
        var receiptOwnerId = confirmed
            ? receipt.ConfirmedLegalEntityId
            : (await warehouses.LockByStoreAndIdAsync(storeId, receipt.WarehouseId, ct))?.LegalEntityId;

        if (!receiptOwnerId.HasValue || receiptOwnerId.Value <= 0)
            throw Block("ReceiptOwnerMissing", "Không xác định được chủ thể sở hữu phiếu nhập.",
                InputInvoiceBuyerOwnerResolutionStatus.NotEvaluated, null,
                invoice.ResolvedBuyerLegalEntityId, invoice,
                TaxCodeIdentityNormalizer.Normalize(invoice.BuyerTaxCode));

        var resolution = await resolver.ResolveWithinTransactionAsync(storeId, invoice, ct);
        if (resolution.Status != InputInvoiceBuyerOwnerResolutionStatus.Resolved || !resolution.LegalEntityId.HasValue)
            throw Block(resolution.Status.ToString(), MessageFor(resolution.Status),
                resolution.Status, receiptOwnerId, resolution.LegalEntityId,
                invoice, resolution.NormalizedBuyerTaxCode);

        if (resolution.LegalEntityId.Value != receiptOwnerId.Value)
            throw Block("BuyerOwnerMismatch",
                "MST người mua trên hóa đơn không khớp chủ thể sở hữu phiếu nhập.",
                resolution.Status, receiptOwnerId, resolution.LegalEntityId,
                invoice, resolution.NormalizedBuyerTaxCode);

        return new(receiptOwnerId.Value, resolution.LegalEntityId, confirmed);
    }

    public async Task<InputInvoiceReceiptOwnerDecision> ValidateConfirmWithinTransactionAsync(
        int storeId,
        StockDocument receipt,
        CancellationToken ct = default)
    {
        EnsureReceipt(storeId, receipt);
        var warehouse = await warehouses.LockByStoreAndIdAsync(storeId, receipt.WarehouseId, ct);
        if (warehouse is null || warehouse.LegalEntityId <= 0)
            throw Block("ReceiptOwnerMissing", "Kho nhận chưa có chủ thể pháp lý hợp lệ.",
                InputInvoiceBuyerOwnerResolutionStatus.NotEvaluated, null, null);
        return new(warehouse.LegalEntityId, null, false);
    }

    private static void EnsureReceipt(int storeId, StockDocument receipt)
    {
        if (receipt.StoreId != storeId || receipt.Type != StockDocumentType.Receipt)
            throw new BusinessRuleException("Chứng từ hiện tại không phải phiếu nhập cùng cửa hàng.");
    }

    private static InputInvoiceOwnerGuardException Block(
        string reason, string message, InputInvoiceBuyerOwnerResolutionStatus status,
        int? receiptOwner, int? invoiceOwner,
        InputInvoiceHead? attemptedInvoice = null,
        string? normalizedBuyerTaxCode = null)
        => new(reason, message, status, receiptOwner, invoiceOwner,
            attemptedInvoice, normalizedBuyerTaxCode);

    private static string MessageFor(InputInvoiceBuyerOwnerResolutionStatus status) => status switch
    {
        InputInvoiceBuyerOwnerResolutionStatus.MissingBuyerTaxCode =>
            "Hóa đơn thiếu MST người mua nên không thể liên kết.",
        InputInvoiceBuyerOwnerResolutionStatus.NotFound =>
            "Không tìm thấy chủ thể pháp lý cùng cửa hàng khớp MST người mua.",
        InputInvoiceBuyerOwnerResolutionStatus.Ambiguous =>
            "MST người mua khớp nhiều chủ thể pháp lý; không thể tự chọn chủ thể.",
        _ => "Chưa xác định được chủ thể người mua của hóa đơn."
    };
}
