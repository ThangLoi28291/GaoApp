using Microsoft.AspNetCore.Authorization;

namespace GaoApp.Infrastructure.Security;

/// <summary>
/// Requirement đại diện cho 1 permission code cần được kiểm tra.
/// Ví dụ:
///     catalog.category.view
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permissionCode)
    {
        PermissionCode = permissionCode;
    }

    /// <summary>
    /// Permission code chuẩn hoặc policy name đang được authorize.
    /// </summary>
    public string PermissionCode { get; }
}