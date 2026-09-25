using System.Security.Claims;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Accounts;

public sealed record EmployeeProfile(string UserName, string DisplayName, string? Email,
    string RoleName, string StoreName, string? PositionName, string? PhoneNumber, DateTime? JoinedDate)
{
    public string Initials => string.Concat(DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .TakeLast(2).Select(part => System.Globalization.StringInfo.GetNextTextElement(part))).ToUpperInvariant();
}

public enum PasswordChangeOutcome { Changed, InvalidSession, IncorrectPassword, ReusedPassword, Conflict }

/// <summary>Self-service identity always comes from the authenticated principal, never a posted user/store id.</summary>
public sealed class EmployeeAccountService(AppDbContext db, ITenantContext tenant, IPasswordHasher passwords)
{
    private IQueryable<UserInStore> Memberships(ClaimsPrincipal principal)
    {
        var userId = int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        var storeId = int.TryParse(principal.FindFirstValue("store_id"), out var store) ? store : 0;
        var valid = principal.Identity?.IsAuthenticated == true && userId > 0 && storeId > 0 && tenant.StoreId == storeId;
        return db.UserInStores.Include(x => x.User).Include(x => x.Role).Include(x => x.Store)
            .Where(x => valid && x.UserId == userId && x.StoreId == storeId && x.IsActive && !x.IsDeleted &&
                x.User.IsActive && !x.User.IsDeleted && x.Role.StoreId == storeId && !x.Role.IsDeleted);
    }

    private static bool HasCurrentCredentials(ClaimsPrincipal principal, UserInStore membership)
        => AuthSessionStamp.Matches(principal.FindFirstValue(AuthSessionStamp.ClaimType) ?? "",
            AuthSessionStamp.Create(membership.User, membership));

    public async Task<EmployeeProfile?> ProfileAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var member = await Memberships(principal).AsNoTracking().SingleOrDefaultAsync(ct);
        if (member is null || !HasCurrentCredentials(principal, member)) return null;
        var user = member.User;
        return new(user.UserName, string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName,
            user.Email, member.Role.Name, member.Store?.Name ?? "Cửa hàng hiện tại",
            member.PositionName, member.PhoneNumber, member.JoinedDate);
    }

    public async Task<PasswordChangeOutcome> ChangePasswordAsync(ClaimsPrincipal principal,
        string currentPassword, string newPassword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length is < 12 or > 128)
            throw new ArgumentException("Mật khẩu phải có từ 12 đến 128 ký tự.", nameof(newPassword));

        var member = await Memberships(principal).SingleOrDefaultAsync(ct);
        if (member is null || !HasCurrentCredentials(principal, member)) return PasswordChangeOutcome.InvalidSession;
        if (!passwords.Verify(currentPassword, member.User.PasswordHash)) return PasswordChangeOutcome.IncorrectPassword;
        if (passwords.Verify(newPassword, member.User.PasswordHash)) return PasswordChangeOutcome.ReusedPassword;

        member.User.PasswordHash = passwords.Hash(newPassword);
        member.User.UpdatedAtUtc = DateTime.UtcNow;
        member.User.UpdatedBy = member.UserId;
        try
        {
            // The user's rowversion prevents two requests verified against the old password from both succeeding.
            await db.SaveChangesAsync(ct);
            return PasswordChangeOutcome.Changed;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(member.User).State = EntityState.Detached;
            return PasswordChangeOutcome.Conflict;
        }
    }
}
