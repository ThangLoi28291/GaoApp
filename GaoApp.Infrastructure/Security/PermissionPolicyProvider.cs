using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace GaoApp.Infrastructure.Security;

/// <summary>
/// Policy provider động cho permission authorization.
/// 
/// Cách hoạt động:
/// - nếu policy đã được đăng ký sẵn trong AddAuthorization => dùng policy đó
/// - nếu chưa có => tự động coi policyName là permission code
/// </summary>
public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        : base(options)
    {
    }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (string.IsNullOrWhiteSpace(policyName))
        {
            return await base.GetPolicyAsync(policyName);
        }

        // Ưu tiên policy tĩnh nếu đã tồn tại.
        var existingPolicy = await base.GetPolicyAsync(policyName);
        if (existingPolicy != null)
        {
            return existingPolicy;
        }

        // Nếu chưa có policy tĩnh thì coi policyName là permission code.
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();

        return policy;
    }
}