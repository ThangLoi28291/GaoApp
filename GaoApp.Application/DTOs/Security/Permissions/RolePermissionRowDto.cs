namespace GaoApp.Application.DTOs.Security.Permissions;

/// <summary>
/// Một dòng trong permission matrix, thường đại diện cho entity hoặc feature.
/// Ví dụ: Category / Product / Supplier.
/// </summary>
public class RolePermissionRowDto
{
    /// <summary>
    /// Tên module chuẩn hóa từ permission code. Ví dụ: catalog, inventory...
    /// </summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>
    /// Tên entity/feature. Ví dụ: category, product...
    /// </summary>
    public string Entity { get; set; } = string.Empty;

    /// <summary>
    /// Nhãn hiển thị đẹp cho UI. Ví dụ: Category.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Các ô action tương ứng trên dòng này.
    /// </summary>
    public List<RolePermissionCellDto> Cells { get; set; } = new();
}