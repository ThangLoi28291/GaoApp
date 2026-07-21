using GaoApp.Application.Common;

namespace GaoApp.Application.DTOs.Security.UserInStores;

/// <summary>
/// Dữ liệu tổng cho màn UserInStore Index.
/// </summary>
public class UserInStoreIndexVm
{
    public UserInStoreIndexQueryDto Query { get; set; } = new();

    public PagedResult<UserInStoreListItemDto> Items { get; set; } = new();

    /// <summary>
    /// Danh sách role để bind dropdown filter.
    /// </summary>
    public List<RoleLookupItemDto> Roles { get; set; } = new();

    public int TotalActiveUsers { get; set; }

    public int TotalInactiveUsers { get; set; }
}