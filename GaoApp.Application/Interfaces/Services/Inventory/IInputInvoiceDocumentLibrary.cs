using GaoApp.Application.DTOs.Inventory.InputInvoices;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInputInvoiceDocumentLibrary
{
    Task<IReadOnlyList<InputInvoicePickerCandidateDto>> BrowseAsync(
        string normalizedSupplierTaxCode,
        InputInvoicePickerBrowseRequest request,
        CancellationToken ct = default);

    Task<InputInvoicePickerCandidateDto> ResolveAsync(
        string normalizedSupplierTaxCode,
        string documentKey,
        CancellationToken ct = default);

    Task<InputInvoicePdfPreviewDto> GetPdfAsync(
        string normalizedSupplierTaxCode,
        string documentKey,
        CancellationToken ct = default);

    Task<byte[]> GetXmlBytesAsync(
        string normalizedSupplierTaxCode,
        string documentKey,
        CancellationToken ct = default);
}
