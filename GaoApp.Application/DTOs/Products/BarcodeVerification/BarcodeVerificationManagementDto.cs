using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Products.BarcodeVerification;

public class BarcodeVerificationManagementDto
{
    public int Id { get; set; }

    public int ProductVariantId { get; set; }

    public int ProductUnitConversionId { get; set; }

    public int? StockDocumentId { get; set; }

    public string ProductNameSnapshot { get; set; } = default!;

    public string UnitNameSnapshot { get; set; } = default!;

    public decimal FactorSnapshot { get; set; }

    public string? SuggestedBarcode { get; set; }

    public BarcodeVerificationRequestType RequestType { get; set; }

    public BarcodeVerificationRequestStatus Status { get; set; }

    public string RequestTypeText { get; set; } = default!;

    public string StatusText { get; set; } = default!;

    public string? EmployeeNote { get; set; }

    public string? ManagerNote { get; set; }

    public int RequestedByUserId { get; set; }

    public string? RequestedByUserName { get; set; }

    public DateTime RequestedAtUtc { get; set; }

    public int? ResolvedByUserId { get; set; }

    public string? ResolvedByUserName { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }

    public int? CreatedBarcodeId { get; set; }

    public string? StockDocumentNo { get; set; }

    public string? WarehouseName { get; set; }
}