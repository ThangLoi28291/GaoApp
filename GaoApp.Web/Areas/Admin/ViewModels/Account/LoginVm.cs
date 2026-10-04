using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Entities;

namespace GaoApp.Web.Areas.Admin.ViewModels.Account;

/// <summary>
/// ViewModel cho màn hình login POS.
/// 
/// Phase DeviceKey:
/// - Nếu thiết bị đã ghép POS: chỉ nhập username/password.
/// - Nếu thiết bị chưa ghép POS: cho Admin/Manager chọn terminal để ghép lại.
/// </summary>
public class LoginVm
{
    public bool ManageTerminals { get; set; }

    [StringLength(100, ErrorMessage = "Khóa máy tối đa 100 ký tự.")]
    public string? PairingKey { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
    [StringLength(100, ErrorMessage = "Tên đăng nhập tối đa 100 ký tự.")]
    public string UserName { get; set; } = default!;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = default!;

    public string? ReturnUrl { get; set; }

    /// <summary>
    /// Terminal được chọn khi thiết bị chưa có cookie POS_DEVICE_KEY.
    /// </summary>
    public int? SelectedTerminalId { get; set; }

    /// <summary>
    /// Tên thiết bị do người dùng nhập để dễ quản lý.
    /// Ví dụ: PC thu ngân, iPhone quản lý.
    /// </summary>
    [StringLength(150, ErrorMessage = "Tên thiết bị tối đa 150 ký tự.")]
    public string? DeviceName { get; set; }

    /// <summary>
    /// Có bắt buộc phải chọn terminal hay không.
    /// Dùng để view biết khi nào hiện khối ghép thiết bị.
    /// </summary>
    public bool RequireTerminalPairing { get; set; }

    /// <summary>
    /// Danh sách terminal đang hoạt động trong store hiện tại.
    /// </summary>
    public List<POSTerminal> AvailableTerminals { get; set; } = new();
}
