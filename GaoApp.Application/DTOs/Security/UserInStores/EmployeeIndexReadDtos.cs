namespace GaoApp.Application.DTOs.Security.UserInStores;

public static class EmployeeIndexLifecycles
{
    public const string All = "all";
    public const string Active = "active";
    public const string Inactive = "inactive";
}

public sealed class EmployeeIndexQueryRequest
{
    public string? Keyword { get; set; }
    public int? RoleId { get; set; }
    public string? Lifecycle { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class EmployeeIndexPageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => (int)Math.Ceiling(
        (double)TotalItems / Math.Max(1, PageSize));
    public bool CanUpdate { get; set; }
    public EmployeeIndexSummaryDto Summary { get; set; } = new();
    public List<EmployeeIndexRoleOptionDto> RoleOptions { get; set; } = new();
    public List<EmployeeIndexItemDto> Items { get; set; } = new();
}

public sealed class EmployeeIndexSummaryDto
{
    public int TotalEmployees { get; set; }
    public int ActiveEmployees { get; set; }
    public int InactiveEmployees { get; set; }
    public int DistinctRoleCount { get; set; }
}

public sealed class EmployeeIndexRoleOptionDto
{
    public int RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public sealed class EmployeeIndexItemDto
{
    public int MappingId { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string? PositionName { get; set; }
    public bool IsActive { get; set; }
    public DateTime? JoinedDate { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? Note { get; set; }
    public bool CanEdit { get; set; }
    public bool CanResetPassword { get; set; }
    public bool CanToggleActive { get; set; }
    public string? ActionLockReason { get; set; }
}
