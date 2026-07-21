using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Constants;

namespace GaoApp.Application.DTOs.POS;

public sealed class CreatePOSCustomerDto
{
    public string Name { get; set; } = default!;

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// Nhóm giá khách hàng:
    /// - RETAIL: khách lẻ
    /// - WHOLESALE: khách sỉ
    /// </summary>
    [StringLength(30)]
    public string PriceTier { get; set; } = CustomerPriceTiers.Retail;
}