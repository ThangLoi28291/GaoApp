namespace GaoApp.Application.DTOs.Invoices;

/// <summary>
/// Kết quả preflight tồn có hóa đơn đầu vào trước khi phát hành HĐĐT.
/// Đây là tồn chứng từ, độc lập với tồn vật lý trong InventoryBalance.
/// </summary>
public sealed class InvoiceInputStockAvailabilityDto
{
    public int InvoiceHeadId { get; init; }

    public bool IsSufficient => Lines.All(x => x.IsSufficient);

    public IReadOnlyList<InvoiceInputStockAvailabilityLineDto> Lines { get; init; }
        = Array.Empty<InvoiceInputStockAvailabilityLineDto>();

    /// <summary>Tồn khả dụng quy đổi về đơn vị từng dòng để kiểm tra trước phát hành.</summary>
    public IReadOnlyList<InvoiceDetailStockLineDto> DetailLines { get; init; }
        = Array.Empty<InvoiceDetailStockLineDto>();
}

public sealed class InvoiceDetailStockLineDto
{
    public int InvoiceDetailId { get; init; }
    public int ProductVariantId { get; init; }
    public int WarehouseId { get; init; }
    public string WarehouseName { get; init; } = string.Empty;
    public string UnitName { get; init; } = string.Empty;
    public string BaseUnitName { get; init; } = string.Empty;
    public decimal UnitFactor { get; init; } = 1m;
    public decimal AvailableQuantity { get; init; }
    /// <summary>Tổng cần và thiếu tính tất cả dòng cùng sản phẩm, cùng kho trong hóa đơn.</summary>
    public decimal RequiredQuantity { get; init; }
    public decimal ShortageQuantity { get; init; }
    public int SharedLineCount { get; init; }
}

public sealed class InvoiceInputStockAvailabilityLineDto
{
    public int WarehouseId { get; init; }

    public int ProductVariantId { get; init; }

    public string ItemName { get; init; } = string.Empty;

    public decimal RequiredBaseQuantity { get; init; }

    public decimal EligibleInboundBaseQuantity { get; init; }

    public decimal CommittedOutboundBaseQuantity { get; init; }

    public decimal AvailableBaseQuantity =>
        EligibleInboundBaseQuantity - CommittedOutboundBaseQuantity;

    public decimal ShortageBaseQuantity =>
        Math.Max(0m, RequiredBaseQuantity - AvailableBaseQuantity);

    public bool IsSufficient => ShortageBaseQuantity <= 0m;
}
