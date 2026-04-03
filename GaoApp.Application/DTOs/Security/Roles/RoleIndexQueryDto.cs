namespace GaoApp.Application.DTOs.Security.Roles;

/// <summary>
/// Điều kiện truy vấn danh sách Role ở màn hình Index.
/// </summary>
public class RoleIndexQueryDto
{
    /// <summary>
    /// Từ khóa tìm theo tên role / code.
    /// </summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// Lọc trạng thái active/inactive. Null = tất cả.
    /// </summary>
    public bool? IsActive { get; set; }

    /// <summary>
    /// Trang hiện tại.
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Số dòng mỗi trang.
    /// </summary>
    public int PageSize { get; set; } = 10;
}