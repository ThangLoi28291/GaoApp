namespace GaoApp.Application.DTOs.POS;

public sealed class POSCustomerSearchItemDto
{
    public int CustomerId { get; set; }
    public bool AskBeforePrintingReceipt { get; set; }

    public string Name { get; set; } = default!;

    public string? Phone { get; set; }

    public string? Address { get; set; }

    /// <summary>
    /// Nhóm giá của khách:
    /// - RETAIL: khách lẻ
    /// - WHOLESALE: khách sỉ
    /// </summary>
    public string PriceTier { get; set; } = "RETAIL";

    public string PriceTierText =>
        PriceTier == "WHOLESALE" ? "Khách sỉ" : "Khách lẻ";
    /// <summary>
    /// true: áp lại giá cho các dòng hiện có theo khách mới.
    /// false: chỉ đổi khách, giữ nguyên giá đang bán.
    /// </summary>
    public bool RepriceExistingLines { get; set; } = false;

    public string DisplayText =>
        string.IsNullOrWhiteSpace(Phone)
            ? $"{Name} - {PriceTierText}"
            : $"{Name} - {Phone} - {PriceTierText}";
}
