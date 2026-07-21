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
    public List<POSShiftDenominationRequest> Denominations { get; set; } = new();
    /// <summary>
    /// Mở ca từ phiếu đã tạo sẵn.
    /// Ưu tiên dùng SlipId.
    /// </summary>
    public int? HandoverSlipId { get; set; }

    /// <summary>
    /// Hỗ trợ quét barcode.
    /// Ví dụ:
    /// POS-HANDOVER:HOS-20260604-0001
    /// </summary>
    public string? HandoverBarcodeValue { get; set; }
}
public class POSShiftDenominationRequest
{
    public int DenominationValue { get; set; }
    public int Quantity { get; set; }
}