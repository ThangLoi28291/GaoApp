using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.POSShifts;

public class OpenShiftRequest
{
    [Range(0, double.MaxValue, ErrorMessage = "Tiền đầu ca không được âm.")]
    public decimal OpeningCash { get; set; }

    [StringLength(300)]
    public string? Note { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn kho xuất bán cho ca POS.")]
    public int WarehouseId { get; set; }
}