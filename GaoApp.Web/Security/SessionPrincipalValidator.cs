using System.Security.Claims;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Security;

public sealed class SessionPrincipalValidator(AppDbContext db, ITenantContext tenant)
{
    public async Task<bool> ValidateAsync(ClaimsPrincipal? principal, CancellationToken ct = default)
    {
        if (principal?.Identity?.IsAuthenticated != true ||
            !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return false;

        var stamp = principal.FindFirstValue(AuthSessionStamp.ClaimType);
        // Pre-upgrade sessions must reauthenticate; never accept unstamped cookies.
        if (string.IsNullOrWhiteSpace(stamp)) return false;

        if (tenant.IsHostAdmin)
        {
            var host = await db.Users.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == userId && x.IsActive && !x.IsDeleted && x.IsHostAdmin, ct);
            return host is not null && AuthSessionStamp.Matches(stamp, AuthSessionStamp.Create(host));
        }

        if (tenant.StoreId is not > 0 ||
            !int.TryParse(principal.FindFirstValue("store_id"), out var cookieStoreId) ||
            cookieStoreId != tenant.StoreId.Value)
            return false;

        var membership = await db.UserInStores.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.User).Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.StoreId == cookieStoreId && x.UserId == userId &&
                x.IsActive && !x.IsDeleted && x.User.IsActive && !x.User.IsDeleted &&
                x.Role.StoreId == cookieStoreId && !x.Role.IsDeleted, ct);

        if (membership is null ||
            principal.FindFirstValue("role_id") != membership.RoleId.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            principal.FindFirstValue(ClaimTypes.Role) != membership.Role.Code)
            return false;

        return AuthSessionStamp.Matches(stamp, AuthSessionStamp.Create(membership.User, membership));
    }
}
