using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Returns;

public sealed class CreateSalesReturnLineRequest
{
    public int OrderLineId { get; set; }

    [Range(0.000001, double.MaxValue, ErrorMessage = "ReturnQuantity phải > 0.")]
    public decimal ReturnQuantity { get; set; }

    public decimal? ReturnBaseQuantity { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "RefundUnitAmount không hợp lệ.")]
    public decimal RefundUnitAmount { get; set; }

    /// <summary>
    /// Bắt buộc client phải gửi rõ:
    /// 0 = NoRestock
    /// 1 = Restock
    /// Nullable để phân biệt:
    /// - client thật sự chọn
    /// - client quên không gửi field này
    /// </summary>
    [Required(ErrorMessage = "Phải chọn cách xử lý hàng trả.")]
    public SalesReturnLineAction? Action { get; set; }

    public string? Reason { get; set; }
}