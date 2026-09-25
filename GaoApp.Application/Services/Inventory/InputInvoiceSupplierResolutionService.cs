using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public sealed class InputInvoiceSupplierResolutionService
    : IInputInvoiceSupplierResolutionService
{
    private readonly IInputInvoiceRepository _repository;
    private readonly ICurrentUser _currentUser;

    public InputInvoiceSupplierResolutionService(
        IInputInvoiceRepository repository,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task BindCanonicalSupplierAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default)
    {
        ValidateIds(storeId, stockDocumentId, inputInvoiceHeadId);
        await _repository.BeginSupplierResolutionTransactionAsync(ct);
        try
        {
            var receipt = await _repository.LockReceiptForInputInvoiceMutationAsync(
                storeId, stockDocumentId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
            await BindCoreAsync(
                storeId, stockDocumentId, inputInvoiceHeadId, receipt, ct);
            await _repository.CommitSupplierResolutionTransactionAsync(ct);
        }
        catch
        {
            await _repository.RollbackSupplierResolutionTransactionAsync(ct);
            throw;
        }
    }

    public async Task BindCanonicalSupplierWithinTransactionAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default)
    {
        ValidateIds(storeId, stockDocumentId, inputInvoiceHeadId);
        var receipt = await _repository.GetReceiptForSupplierResolutionAsync(
            storeId, stockDocumentId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập kho.");
        await BindCoreAsync(
            storeId, stockDocumentId, inputInvoiceHeadId, receipt, ct);
    }

    public async Task EnsureReceiptCanBeConfirmedAsync(
        int storeId,
        int stockDocumentId,
        int? receiptSupplierId,
        CancellationToken ct = default)
    {
        if (storeId <= 0) throw new BusinessRuleException("StoreId không hợp lệ.");
        if (stockDocumentId <= 0) throw new BusinessRuleException("Phiếu nhập không hợp lệ.");
        var linked = await _repository.GetLinkedInvoicesForSupplierResolutionAsync(
            storeId, stockDocumentId, ct);
        PurchaseReceiptSupplierInvoiceConsistencyPolicy.EnsureCanConfirm(
            receiptSupplierId,
            linked.Select(x => x.ResolvedSupplierId).ToList());
    }

    private int RequireActor()
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue ||
            _currentUser.UserId.Value <= 0)
            throw new BusinessRuleException("Cần người dùng đã đăng nhập để liên kết hóa đơn.");
        return _currentUser.UserId.Value;
    }

    private async Task BindCoreAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        StockDocument receipt,
        CancellationToken ct)
    {
        var actor = RequireActor();
        if (!receipt.SupplierId.HasValue || receipt.Supplier is null)
            throw new BusinessRuleException(
                "Vui lòng chọn nhà cung cấp trước khi chọn hóa đơn.");
        var receiptTaxCode = TaxCodeIdentityNormalizer.Normalize(receipt.Supplier.TaxCode)
            ?? throw new BusinessRuleException(
                "Nhà cung cấp chưa có mã số thuế. Vui lòng cập nhật danh mục nhà cung cấp trước khi chọn hóa đơn.");
        var invoice = await _repository.LockForSupplierResolutionAsync(
            storeId, inputInvoiceHeadId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy hóa đơn trong cửa hàng hiện tại.");
        var invoiceTaxCode = TaxCodeIdentityNormalizer.Normalize(invoice.SellerTaxCode);
        if (!string.Equals(receiptTaxCode, invoiceTaxCode, StringComparison.Ordinal))
            throw new BusinessRuleException(
                "MST người bán trên XML không khớp nhà cung cấp của phiếu nhập.");
        if (invoice.ResolvedSupplierId.HasValue &&
            invoice.ResolvedSupplierId.Value != receipt.SupplierId.Value)
            throw new BusinessRuleException(
                "Nhà cung cấp chuẩn của hóa đơn không khớp phiếu nhập. Dữ liệu cần được kiểm tra.");

        if (!invoice.ResolvedSupplierId.HasValue)
        {
            var previousStatus = invoice.SupplierResolutionStatus;
            invoice.ResolvedSupplierId = receipt.SupplierId.Value;
            invoice.SupplierResolutionStatus = InputInvoiceSupplierResolutionStatus.Resolved;
            invoice.SupplierResolutionUpdatedAtUtc = DateTime.UtcNow;
            await _repository.AddSupplierResolutionEventAsync(
                new InputInvoiceSupplierResolutionEvent
                {
                    StoreId = storeId,
                    InputInvoiceHeadId = invoice.Id,
                    StockDocumentId = stockDocumentId,
                    EventType = InputInvoiceSupplierResolutionEventType.AutoResolved,
                    PreviousStatus = previousStatus,
                    NewStatus = InputInvoiceSupplierResolutionStatus.Resolved,
                    NewSupplierId = receipt.SupplierId.Value,
                    CandidateCount = 1,
                    ActorUserId = actor,
                    CreatedAtUtc = DateTime.UtcNow
                }, ct);
            await _repository.SaveChangesAsync(ct);
        }
        else if (invoice.SupplierResolutionStatus != InputInvoiceSupplierResolutionStatus.Resolved)
        {
            invoice.SupplierResolutionStatus = InputInvoiceSupplierResolutionStatus.Resolved;
            invoice.SupplierResolutionUpdatedAtUtc = DateTime.UtcNow;
            await _repository.SaveChangesAsync(ct);
        }
    }

    private static void ValidateIds(int storeId, int stockDocumentId, int inputInvoiceHeadId)
    {
        if (storeId <= 0) throw new BusinessRuleException("StoreId không hợp lệ.");
        if (stockDocumentId <= 0) throw new BusinessRuleException("Phiếu nhập không hợp lệ.");
        if (inputInvoiceHeadId <= 0) throw new BusinessRuleException("Hóa đơn không hợp lệ.");
    }
}
