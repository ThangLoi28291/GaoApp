namespace GaoApp.Application.DTOs.Security.Permissions;

/// <summary>
/// Dữ liệu tổng cho màn gán permission cho role.
/// </summary>
public class RolePermissionMatrixDto
{
    /// <summary>
    /// Role hiện đang được cấu hình permission.
    /// </summary>
    public int RoleId { get; set; }

    public string RoleName { get; set; } = string.Empty;

    public string RoleCode { get; set; } = string.Empty;

    public bool RoleIsActive { get; set; }

    /// <summary>
    /// Số user đang dùng role này.
    /// </summary>
    public int UserCount { get; set; }

    /// <summary>
    /// Tổng số permission đang được chọn.
    /// </summary>
    public int SelectedPermissionCount { get; set; }

    /// <summary>
    /// Tổng số permission khả dụng trong catalog.
    /// </summary>
    public int TotalPermissionCount { get; set; }

    /// <summary>
    /// Danh sách group permission để hiển thị theo accordion/card.
    /// </summary>
    public List<RolePermissionGroupDto> Groups { get; set; } = new();
}