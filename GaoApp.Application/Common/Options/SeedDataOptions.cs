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
    /// Có seed admin mặc định hay không.
    /// </summary>
    public bool EnableDefaultAdminSeed { get; set; } = true;
}