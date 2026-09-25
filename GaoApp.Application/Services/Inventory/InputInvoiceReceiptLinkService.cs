using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public sealed class InputInvoiceReceiptLinkService(
    IInputInvoiceRepository repository,
    IInputInvoiceReceiptOwnerGuard ownerGuard,
    IInputInvoiceSupplierResolutionService supplierResolution,
    IInputInvoiceReconciliationService? reconciliation = null)
    : IInputInvoiceReceiptLinkService
{
    public async Task<bool> LinkWithinTransactionAsync(
        int storeId,
        StockDocument receipt,
        InputInvoiceHead invoice,
        string? note,
        CancellationToken ct = default)
        => await LinkWithinTransactionAsync(
            storeId,
            receipt,
            invoice,
            note,
            refreshReconciliation: true,
            writeLinkAudit: true,
            ct);

    public async Task<bool> LinkWithinTransactionAsync(
        int storeId,
        StockDocument receipt,
        InputInvoiceHead invoice,
        string? note,
        bool refreshReconciliation,
        bool writeLinkAudit,
        CancellationToken ct = default)
    {
        await ValidateLinkWithinTransactionAsync(storeId, receipt, invoice, ct);

        var created = await repository.EnsureSingleReceiptInvoiceMapAsync(
            new StockDocumentInputInvoiceMap
            {
                StoreId = storeId,
                StockDocumentId = receipt.Id,
                InputInvoiceHeadId = invoice.Id,
                Note = string.IsNullOrWhiteSpace(note) ? null : note[..Math.Min(note.Length, 1000)]
            }, ct);
        await repository.AddMissingLineMapsAsync(storeId, receipt.Id, ct);
        if (created && writeLinkAudit)
            await repository.AddPurchaseReceiptAuditEventAsync(CreateLinkedEvent(storeId, receipt, invoice), ct);
        if (refreshReconciliation && reconciliation is not null)
            await reconciliation.RefreshWithinTransactionAsync(storeId, receipt.Id, ct);
        return created;
    }

    public async Task ValidateLinkWithinTransactionAsync(
        int storeId,
        StockDocument receipt,
        InputInvoiceHead invoice,
        CancellationToken ct = default)
    {
        await ownerGuard.ValidateLinkWithinTransactionAsync(storeId, receipt, invoice, ct);
        await supplierResolution.BindCanonicalSupplierWithinTransactionAsync(
            storeId, receipt.Id, invoice.Id, ct);
    }

    public async Task ValidateExistingLinksForConfirmWithinTransactionAsync(
        int storeId,
        StockDocument receipt,
        CancellationToken ct = default)
    {
        var owner = await ownerGuard.ValidateConfirmWithinTransactionAsync(storeId, receipt, ct);
        await ValidateExistingLinksForOwnerMutationWithinTransactionAsync(
            storeId, receipt, ct);
        receipt.ConfirmedLegalEntityId = owner.ReceiptOwnerLegalEntityId;
    }

    public async Task ValidateExistingLinksForOwnerMutationWithinTransactionAsync(
        int storeId,
        StockDocument receipt,
        CancellationToken ct = default)
    {
        var invoices = await repository.GetLinkedInvoicesForSupplierResolutionAsync(
            storeId, receipt.Id, ct);
        if (invoices.Count > 1)
            throw new BusinessRuleException("Phiếu nhập chỉ được liên kết tối đa một hóa đơn đầu vào.");
        foreach (var invoice in invoices)
            await ownerGuard.ValidateLinkWithinTransactionAsync(storeId, receipt, invoice, ct);
    }

    private static PurchaseReceiptAuditEvent CreateLinkedEvent(
        int storeId, StockDocument receipt, InputInvoiceHead invoice) => new()
    {
        StoreId = storeId,
        StockDocumentId = receipt.Id,
        EventType = PurchaseReceiptAuditEventType.InputInvoiceLinked,
        Note = "Liên kết hóa đơn đầu vào sau khi kiểm tra chủ thể người mua.",
        ChangedFieldsJson = PurchaseReceiptAuditEvidence.SerializeChangedFields(
            [nameof(StockDocumentInputInvoiceMap.InputInvoiceHeadId), nameof(InputInvoiceHead.ResolvedBuyerLegalEntityId)]),
        NewValuesJson = PurchaseReceiptAuditEvidence.SerializeValues(new Dictionary<string, object?>
        {
            [nameof(StockDocumentInputInvoiceMap.InputInvoiceHeadId)] = invoice.Id,
            [nameof(InputInvoiceHead.ResolvedBuyerLegalEntityId)] = invoice.ResolvedBuyerLegalEntityId,
            [nameof(InputInvoiceHead.BuyerOwnerResolutionStatus)] = invoice.BuyerOwnerResolutionStatus.ToString()
        })
    };
}
