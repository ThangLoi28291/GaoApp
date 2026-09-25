using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Products.BarcodeVerification;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Services.Products;

public sealed record ReceiptBarcodeContext(PurchaseReceiptSource ReceiptSource, StockDocumentStatus Status);

public sealed class ProposeReceiptBarcodeRequest
{
    public int ProductUnitConversionId { get; set; }
    public string Barcode { get; set; } = "";
    public decimal Factor { get; set; }
    public string? Note { get; set; }
    public Guid? LeaseToken { get; set; }
}

public interface IReceiptBarcodeProposalService
{
    Task<ReceiptBarcodeContext> ContextAsync(int storeId, int documentId, CancellationToken ct);
    Task<List<BarcodeVerificationManagementDto>> ListAsync(int storeId, int documentId, CancellationToken ct);
    Task<List<StockDocumentLookupSelect2ItemDto>> SearchAsync(int storeId, int documentId, string term, bool catalogOnly, CancellationToken ct);
    Task<List<StockDocumentLookupSelect2ItemDto>> UnitsAsync(int storeId, int documentId, int variantId, CancellationToken ct);
    Task<StockDocumentLookupSelect2ItemDto> ProposeAsync(int storeId, int documentId, int userId, ProposeReceiptBarcodeRequest request, CancellationToken ct);
    Task<StockDocumentLookupSelect2ItemDto> ProposeWithinTransactionAsync(int storeId, int documentId, int userId, ProposeReceiptBarcodeRequest request, CancellationToken ct);
    Task ResolveAsync(int storeId, int? documentId, int requestId, int userId, BarcodeVerificationRequestStatus decision, string? note, CancellationToken ct);
}
