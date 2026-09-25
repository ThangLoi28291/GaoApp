using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Common.Security;

/// <summary>
/// A credential/state fingerprint carried only inside the encrypted authentication cookie.
/// Uses existing SQL rowversions so password, account and membership changes revoke old cookies.
/// </summary>
public static class AuthSessionStamp
{
    public const string ClaimType = "gaoapp_session_stamp_v1";

    public static string Create(User user, UserInStore? membership = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user.PasswordHash);
        var state = JsonSerializer.SerializeToUtf8Bytes(new
        {
            user.Id, user.PasswordHash, user.RowVersion,
            UserUpdatedTicks = user.UpdatedAtUtc?.Ticks,
            user.IsHostAdmin, user.IsActive, user.IsDeleted,
            MembershipId = membership?.Id,
            membership?.StoreId,
            MembershipVersion = membership?.RowVersion,
            MembershipUpdatedTicks = membership?.UpdatedAtUtc?.Ticks,
            membership?.RoleId,
            RoleCode = membership?.Role?.Code,
            RoleVersion = membership?.Role?.RowVersion
        });
        try { return Convert.ToHexString(SHA256.HashData(state)); }
        finally { CryptographicOperations.ZeroMemory(state); }
    }

    public static bool Matches(string? supplied, string expected)
    {
        if (supplied is null || supplied.Length != 64) return false;
        // ASCII hex is compared without decoding attacker-controlled input.
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(supplied),
            System.Text.Encoding.ASCII.GetBytes(expected));
    }
}
