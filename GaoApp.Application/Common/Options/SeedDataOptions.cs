namespace GaoApp.Application.Common.Options;

/// <summary>
/// Cấu hình seed dữ liệu khởi tạo.
/// </summary>
public class SeedDataOptions
{
    public const string SectionName = "SeedData";

    /// <summary>
    /// Có seed dữ liệu demo hay không.
    /// </summary>
    public bool EnableDemoSeed { get; set; }

    /// <summary>
    /// Mật khẩu dùng khi chủ động bật demo seed. Không lưu giá trị thật trong source;
    /// truyền bằng biến môi trường SeedData__DemoUserPassword.
    /// </summary>
    public string? DemoUserPassword { get; set; }

    /// <summary>
    /// Có seed admin mặc định hay không.
    /// </summary>
    public bool EnableDefaultAdminSeed { get; set; }

    /// <summary>
    /// Rule cấu hình duy nhất của Demo Seed. Mật khẩu chỉ bắt buộc khi chủ động
    /// bật seed demo; giá trị mật khẩu không được đưa vào thông báo lỗi.
    /// </summary>
    public bool HasValidConfiguration()
        => !EnableDemoSeed
            || !string.IsNullOrWhiteSpace(DemoUserPassword);
}
