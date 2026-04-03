namespace GaoApp.Web.Areas.Admin.ViewModels.POS;

public class POSHeaderContextViewModel
{
    public int? StoreId { get; set; }
    public string StoreName { get; set; } = "Chưa xác định store";

    public int? TerminalId { get; set; }
    public string TerminalName { get; set; } = "Chưa xác định terminal";
    public string? TerminalCode { get; set; }

    public int? UserId { get; set; }
    public string UserName { get; set; } = "Chưa xác định nhân viên";

    public int? ShiftId { get; set; }
    public string ShiftCode { get; set; } = "Chưa mở ca";
    public string ShiftStatusText { get; set; } = "NoShift";

    public string? WarehouseName { get; set; }
    public DateTime? OpenedAtUtc { get; set; }

    public bool HasOpenShift => ShiftId.HasValue && string.Equals(ShiftStatusText, "Open", StringComparison.OrdinalIgnoreCase);
}