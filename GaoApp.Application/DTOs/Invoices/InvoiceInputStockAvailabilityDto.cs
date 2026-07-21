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
