using System.Security.Claims;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Security;

public sealed class SessionPrincipalValidator(AppDbContext db, ITenantContext tenant)
{
    public async Task<bool> ValidateAsync(ClaimsPrincipal? principal, CancellationToken ct = default, string? deviceKey = null)
    {
        if (tenant.IsHostAdmin && tenant is ITenantContextWriter reset) reset.SetAuthenticatedHostAdmin(false);
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
            var valid = host is not null && AuthSessionStamp.Matches(stamp, AuthSessionStamp.Create(host));
            if (valid && tenant is ITenantContextWriter authorized) authorized.SetAuthenticatedHostAdmin(true);
            return valid;
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

        if (!AuthSessionStamp.Matches(stamp, AuthSessionStamp.Create(membership.User, membership))) return false;

        // The existing admin-only management login deliberately has no POS device.
        if (principal.FindFirstValue("terminal_id") == "0" &&
            principal.FindFirstValue(PosDeviceSession.ManagementClaim) == "1" && membership.Role.Code == "ADMIN" &&
            principal.FindFirst(PosDeviceSession.IdClaim) == null && principal.FindFirst(PosDeviceSession.StampClaim) == null)
            return true;

        // Legacy tickets must sign in once to acquire a device binding. Clearing or
        // swapping the pairing cookie cannot revive a revoked authentication ticket.
        if (PosDeviceSession.DeviceId(principal) is not { } deviceId || string.IsNullOrWhiteSpace(deviceKey) ||
            !int.TryParse(principal.FindFirstValue("terminal_id"), out var terminalId) || terminalId <= 0)
            return false;
        var device = await db.POSTerminalDevices.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == deviceId && x.StoreId == cookieStoreId && x.TerminalId == terminalId &&
                x.DeviceKey == deviceKey && x.IsActive && !x.IsDeleted &&
                x.Terminal.StoreId == cookieStoreId && x.Terminal.IsActive && !x.Terminal.IsDeleted &&
                x.Terminal.Status == GaoApp.Domain.Enums.POSTerminalStatus.Active, ct);
        return device != null && PosDeviceSession.Matches(principal, device);
    }
}
