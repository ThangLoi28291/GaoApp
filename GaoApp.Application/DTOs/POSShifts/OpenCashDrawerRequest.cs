using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.POSShifts;

public sealed class OpenCashDrawerRequest
{
    [Range(1, int.MaxValue)]
    public int ShiftId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập lý do mở két.")]
    [StringLength(300, ErrorMessage = "Lý do mở két tối đa 300 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}
