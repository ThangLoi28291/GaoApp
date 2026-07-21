namespace GaoApp.Application.DTOs.Security.UserInStores;

/// <summary>
/// Điều kiện lọc cho màn UserInStore Index.
/// </summary>
public class UserInStoreIndexQueryDto
{
    /// <summary>
    /// Từ khóa tìm theo username / tên / email.
    /// </summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// Lọc theo role. Null = tất cả.
    /// </summary>
    public int? RoleId { get; set; }

    /// <summary>
    /// Lọc theo trạng thái active. Null = tất cả.
    /// </summary>
    public bool? IsActive { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;
}