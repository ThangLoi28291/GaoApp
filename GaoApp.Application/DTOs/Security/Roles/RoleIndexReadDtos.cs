namespace GaoApp.Application.DTOs.Security.Roles;

public static class RoleIndexTypes
{
    public const string All = "all";
    public const string System = "system";
    public const string Custom = "custom";
}

public static class RoleIndexLifecycles
{
    public const string All = "all";
    public const string Active = "active";
    public const string Inactive = "inactive";
}

public sealed class RoleIndexQueryRequest
{
    public string? Keyword { get; set; }
    public string? Type { get; set; }
    public string? Lifecycle { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class RoleIndexPageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => (int)Math.Ceiling(
        (double)TotalItems / Math.Max(1, PageSize));
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }
    public bool CanPermissions { get; set; }
    public RoleIndexSummaryDto Summary { get; set; } = new();
    public List<RoleIndexItemDto> Items { get; set; } = new();
}

public sealed class RoleIndexSummaryDto
{
    public int TotalRoles { get; set; }
    public int ActiveRoles { get; set; }
    public int InactiveRoles { get; set; }
    public int SystemRoles { get; set; }
}

public sealed class RoleIndexItemDto
{
    public int RoleId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSystemRole { get; set; }
    public bool IsActive { get; set; }
    public int PermissionCount { get; set; }
    public int ActiveUserCount { get; set; }
    public int AssignedUserCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
