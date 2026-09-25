using GaoApp.Application.DTOs.Inventory.InputInvoices;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInputInvoiceItemCatalogMappingService
{
    Task<IReadOnlyDictionary<int, InputInvoiceItemCatalogResolutionDto>>
        ResolveForReceiptAsync(
            int storeId,
            int stockDocumentId,
            CancellationToken ct = default);

    Task<InputInvoiceItemCatalogResolutionDto> ConfirmWithinTransactionAsync(
        int storeId,
        int stockDocumentId,
        int stockDocumentLineId,
        int inputInvoiceDetailId,
        int productVariantId,
        int productUnitConversionId,
        string? expectedMappingRowVersion,
        CancellationToken ct = default);

    Task AutoApplyKnownMappingsWithinTransactionAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default);
}
