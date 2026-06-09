using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.POSShiftHandoverSlips;

public class CancelPOSShiftHandoverSlipRequest
{
    [Required(ErrorMessage = "Vui lòng nhập lý do hủy phiếu.")]
    [StringLength(300)]
    public string Reason { get; set; } = default!;
}