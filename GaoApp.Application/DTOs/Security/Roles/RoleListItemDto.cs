namespace GaoApp.Application.DTOs.Security.Roles;

/// <summary>
/// Dòng dữ liệu hiển thị ở màn danh sách Role.
/// Không trả thẳng entity để tránh lộ field thừa và dễ kiểm soát UI.
/// </summary>
public class RoleListItemDto
{
    /// <summary>
    /// Id role.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Tên role hiển thị cho admin.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mã role / normalized code nếu có.
    /// </summary>
    public string Code { get; set; } = string.Empty;



    /// <summary>
    /// Role có đang hoạt động hay không.
    /// </summary>
    public bool IsActive { get; set; }

    public bool IsSystemRole { get; set; }

    public int PermissionCount { get; set; }
    public int UserCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}