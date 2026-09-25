using GaoApp.Application.DTOs.Inventory.InputInvoices;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInputInvoicePickerService
{
    Task<InputInvoiceAssociationContextDto> GetAssociationContextAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default);

    Task<InputInvoicePickerBrowseResultDto> BrowseAsync(
        int storeId,
        int stockDocumentId,
        InputInvoicePickerBrowseRequest request,
        CancellationToken ct = default);

    Task<InputInvoicePdfPreviewDto> GetPdfAsync(
        int storeId,
        int stockDocumentId,
        string documentKey,
        CancellationToken ct = default);

    Task<InputInvoiceXmlPreviewDto> GetXmlPreviewAsync(
        int storeId,
        int stockDocumentId,
        string documentKey,
        CancellationToken ct = default);

    Task<InputInvoicePdfPreviewDto> GetLinkedPdfAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default);

    Task<InputInvoiceXmlPreviewDto> GetLinkedXmlPreviewAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default);

    Task<bool> UnlinkAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default);

    Task<InputInvoiceAssociationMutationResultDto> UnlinkAsync(
        int storeId,
        int stockDocumentId,
        UnlinkInputInvoiceRequest request,
        CancellationToken ct = default);

    Task<InputInvoiceAssociationMutationResultDto> RelinkAsync(
        int storeId,
        int stockDocumentId,
        RelinkInputInvoiceRequest request,
        CancellationToken ct = default);

    Task<InputInvoicePickerSelectionResultDto> SelectAsync(
        int storeId,
        int stockDocumentId,
        SelectInputInvoiceDocumentRequest request,
        CancellationToken ct = default);

    Task<InputInvoicePickerBrowseResultDto> BrowseForSupplierAsync(
        int storeId,
        int supplierId,
        InputInvoicePickerBrowseRequest request,
        CancellationToken ct = default);

    Task<InputInvoicePickerBrowseResultDto> BrowseForSupplierAndWarehouseAsync(
        int storeId,
        int supplierId,
        int warehouseId,
        InputInvoicePickerBrowseRequest request,
        CancellationToken ct = default)
        => BrowseForSupplierAsync(storeId, supplierId, request, ct);

    Task<InputInvoicePdfPreviewDto> GetPdfForSupplierAsync(
        int storeId,
        int supplierId,
        string documentKey,
        CancellationToken ct = default);

    Task<InputInvoiceXmlPreviewDto> GetXmlPreviewForSupplierAsync(
        int storeId,
        int supplierId,
        string documentKey,
        CancellationToken ct = default);

    Task<InputInvoiceXmlPreviewDto> GetXmlPreviewForSupplierAndWarehouseAsync(
        int storeId,
        int supplierId,
        int warehouseId,
        string documentKey,
        CancellationToken ct = default)
        => GetXmlPreviewForSupplierAsync(storeId, supplierId, documentKey, ct);
}
