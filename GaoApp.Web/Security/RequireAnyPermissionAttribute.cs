using Microsoft.AspNetCore.Authorization;

namespace GaoApp.Web.Security;

/// <summary>For shared read-only lookups and upload staging used by several workflows.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireAnyPermissionAttribute : AuthorizeAttribute
{
    public RequireAnyPermissionAttribute(params string[] permissions)
    {
        if (permissions.Length == 0 || permissions.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one permission is required.", nameof(permissions));
        Policy = "any:" + string.Join('|', permissions);
    }
}
