using GaoApp.Application.Common;

namespace GaoApp.Application.DTOs.Security.Roles;

/// <summary>
/// Dữ liệu tổng cho màn Role Index.
/// </summary>
public class RoleIndexVm
{
    /// <summary>
    /// Điều kiện lọc hiện tại để giữ trạng thái form search.
    /// </summary>
    public RoleIndexQueryDto Query { get; set; } = new();

    /// <summary>
    /// Kết quả phân trang.
    /// </summary>
    public PagedResult<RoleListItemDto> Roles { get; set; } = new();

    /// <summary>
    /// Tổng số role active trong store hiện tại.
    /// </summary>
    public int TotalActiveRoles { get; set; }

    /// <summary>
    /// Tổng số role inactive trong store hiện tại.
    /// </summary>
    public int TotalInactiveRoles { get; set; }
}