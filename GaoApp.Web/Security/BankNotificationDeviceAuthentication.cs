using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Security;

public static class BankNotificationDeviceAuthentication
{
    public static async Task<AuthenticatedBankNotification?> AuthenticateContextAsync(AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var device = await AuthenticateAsync(db, context, ct); if (device is null) return null;
        var account = await db.StoreBankAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == device.StoreId && x.Id == device.BankAccountId && x.IsActive, ct);
        var accountVersion = account?.RowVersion;
        if (accountVersion is not { Length: 8 }) return null;
        return new(device, device.TokenHash, device.BankAccountFingerprint, Convert.ToBase64String(accountVersion), DateTime.UtcNow);
    }
    public static async Task<BankNotificationDevice?> AuthenticateAsync(AppDbContext db, HttpContext context, CancellationToken ct)
    {
        if (!context.Request.IsHttps || db.CurrentStoreId is not > 0) return null;
        var header = context.Request.Headers["X-GaoApp-Notification-Device"];
        if (header.Count != 1) return null;
        var parts = header.ToString().Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 ||
            parts[1].Length != 64 || !parts[1].All(Uri.IsHexDigit)) return null;
        var device = await db.Set<BankNotificationDevice>().SingleOrDefaultAsync(x => x.StoreId == db.CurrentStoreId && x.Id == id && x.Enabled, ct);
        return device is not null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(device.TokenHash),
            Encoding.ASCII.GetBytes(BankNotificationParser.Hash(parts[1]))) ? device : null;
    }
}

// The constructor is server-owned; no HTTP payload can supply an authenticated acceptance proof.
public sealed class AuthenticatedBankNotification
{
    internal AuthenticatedBankNotification(BankNotificationDevice device, string tokenHash, string fingerprint, string accountVersion, DateTime at)
    { Device = device; TokenHash = tokenHash; Fingerprint = fingerprint; AccountVersion = accountVersion; AtUtc = at; BankAccountId = device.BankAccountId; StoreId = device.StoreId; DeviceId = device.Id; }
    internal BankNotificationDevice Device { get; }
    internal string TokenHash { get; }
    internal string Fingerprint { get; }
    internal string AccountVersion { get; }
    internal DateTime AtUtc { get; }
    internal int BankAccountId { get; }
    internal int StoreId { get; }
    internal int DeviceId { get; }
}
