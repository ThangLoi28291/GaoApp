namespace GaoApp.Application.DTOs.Security.Permissions;

/// <summary>
/// Một ô trong ma trận permission.
/// </summary>
public class RolePermissionCellDto
{
    /// <summary>
    /// PermissionId trong DB. Có thể null nếu ô này chỉ là placeholder không tồn tại.
    /// </summary>
    public int? PermissionId { get; set; }

    /// <summary>
    /// Permission code chuẩn. Ví dụ: catalog.category.view
    /// </summary>
    public string? PermissionCode { get; set; }

    /// <summary>
    /// Action chuẩn. Ví dụ: view/create/update/delete...
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Nhãn đẹp cho cột nếu cần.
    /// </summary>
    public string ActionDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Permission này có tồn tại thực sự trong catalog hay không.
    /// Nếu false thì render dấu '-' hoặc disabled.
    /// </summary>
    public bool IsAvailable { get; set; }

    /// <summary>
    /// Role hiện có permission này hay không.
    /// </summary>
    public bool IsChecked { get; set; }
}