using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Hubs;

/// <summary>Fresh, bounded reads for connected clients, independent of their original HTTP request scope.</summary>
public sealed class PosRealtimeSessionValidator(IServiceScopeFactory scopeFactory)
{
    public sealed record Session(string ConnectionId, ClaimsPrincipal Principal, int UserId, int StoreId, int TerminalId);
    public sealed record Access(string PermissionStamp, bool CanBroadcastPayment);

    public async Task<Dictionary<string, Access>> ValidateAsync(IReadOnlyCollection<Session> sessions, CancellationToken ct)
    {
        var valid = new Dictionary<string, Access>(StringComparer.Ordinal);
        foreach (var batch in sessions.Chunk(100))
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stores = batch.Select(x => x.StoreId).Distinct().ToArray();
            var users = batch.Select(x => x.UserId).Distinct().ToArray();
            var terminals = batch.Select(x => x.TerminalId).Distinct().ToArray();
            // Every IgnoreQueryFilters query is explicitly bounded by the identities of this batch.
            var memberships = await db.UserInStores.IgnoreQueryFilters().AsNoTracking()
                .Include(x => x.User).Include(x => x.Role)
                .Where(x => stores.Contains(x.StoreId) && users.Contains(x.UserId) && x.IsActive && !x.IsDeleted &&
                    x.User.IsActive && !x.User.IsDeleted && !x.Role.IsDeleted && x.Role.StoreId == x.StoreId &&
                    db.Stores.IgnoreQueryFilters().Any(s => s.Id == x.StoreId && s.IsActive && !s.IsDeleted))
                .ToListAsync(ct);
            var roleIds = memberships.Select(x => x.RoleId).Distinct().ToArray();
            var grants = await db.RolePermissions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => roleIds.Contains(x.RoleId))
                .Select(x => new { x.Id, x.RoleId, x.Permission.Code }).ToListAsync(ct);
            var activeTerminals = await db.POSTerminals.IgnoreQueryFilters().AsNoTracking()
                .Where(x => stores.Contains(x.StoreId) && terminals.Contains(x.Id) && x.IsActive && !x.IsDeleted &&
                    x.Status == POSTerminalStatus.Active)
                .Select(x => new { x.Id, x.StoreId }).ToListAsync(ct);
            var terminalKeys = activeTerminals.Select(x => (x.StoreId, x.Id)).ToHashSet();
            var membershipMap = memberships.ToDictionary(x => (x.StoreId, x.UserId));
            var accepted = PermissionAliasMap.GetAcceptedCodes(PermissionCodes.Pos.Order.View);
            var paymentCodes = PermissionAliasMap.GetAcceptedCodes(PermissionCodes.Pos.Payment.Create);
            var rolePermissions = grants.GroupBy(x => x.RoleId).ToDictionary(x => x.Key,
                x => x.Select(p => p.Code.Trim().ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal).ToArray());
            var roleStamps = grants.GroupBy(x => x.RoleId).ToDictionary(x => x.Key, x =>
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', x.OrderBy(p => p.Id)
                    .Select(p => p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + p.Code.Trim().ToUpperInvariant()))))));
            foreach (var session in batch)
            {
                if (!terminalKeys.Contains((session.StoreId, session.TerminalId)) ||
                    !membershipMap.TryGetValue((session.StoreId, session.UserId), out var membership) ||
                    !rolePermissions.TryGetValue(membership.RoleId, out var permissions) ||
                    !permissions.Any(p => accepted.Contains(p, StringComparer.OrdinalIgnoreCase)) ||
                    session.Principal.FindFirstValue("role_id") != membership.RoleId.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                    session.Principal.FindFirstValue(ClaimTypes.Role) != membership.Role.Code ||
                    !AuthSessionStamp.Matches(session.Principal.FindFirstValue(AuthSessionStamp.ClaimType),
                        AuthSessionStamp.Create(membership.User, membership))) continue;
                // Any grant change closes the old connection, including removal of a non-POS permission.
                valid[session.ConnectionId] = new(roleStamps[membership.RoleId],
                    permissions.Any(p => paymentCodes.Contains(p, StringComparer.OrdinalIgnoreCase)));
            }
        }
        return valid;
    }
}
