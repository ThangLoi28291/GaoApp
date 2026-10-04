using System.ComponentModel.DataAnnotations;

namespace GaoApp.Web.Areas.Admin.ViewModels.POSTerminals;

public sealed class POSTerminalVm
{
    [Required(ErrorMessage = "Vui lòng nhập mã máy POS.")]
    [RegularExpression("^[A-Za-z0-9_-]{1,30}$", ErrorMessage = "Mã máy gồm tối đa 30 chữ cái, chữ số, dấu gạch ngang hoặc gạch dưới.")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập tên máy POS.")]
    [StringLength(150, ErrorMessage = "Tên máy tối đa 150 ký tự.")]
    public string Name { get; set; } = "";

    [StringLength(300, ErrorMessage = "Ghi chú tối đa 300 ký tự.")]
    public string? Description { get; set; }
}

public sealed class CreatePOSKeyVm
{
    [Range(1, int.MaxValue)]
    public int TerminalId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên thiết bị để cấp khóa.")]
    [StringLength(150, ErrorMessage = "Tên thiết bị tối đa 150 ký tự.")]
    public string DeviceName { get; set; } = "";
}

public sealed record POSDeviceRow(int Id, string? DeviceName, bool IsActive, DateTime CreatedAtUtc);
public sealed record POSTerminalRow(int Id, string Code, string Name, bool IsActive,
    string? Description, List<POSDeviceRow> Devices);
public sealed record POSTerminalIndexVm(List<POSTerminalRow> Terminals);
