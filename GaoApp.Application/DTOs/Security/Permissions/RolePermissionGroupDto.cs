namespace GaoApp.Application.DTOs.Security.Permissions;

/// <summary>
/// Một group permission, ví dụ: Catalog / Inventory / POS...
/// </summary>
public class RolePermissionGroupDto
{
    /// <summary>
    /// Tên group.
    /// </summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>
    /// Thứ tự hiển thị group nếu cần.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Số permission đã chọn trong group.
    /// </summary>
    public int SelectedCount { get; set; }

    /// <summary>
    /// Tổng số permission có trong group.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Các dòng entity/feature trong group.
    /// </summary>
    public List<RolePermissionRowDto> Rows { get; set; } = new();
}