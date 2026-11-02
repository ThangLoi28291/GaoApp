using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Common.Security;

/// <summary>Device identity and credential generation inside the protected login ticket; never the raw key.</summary>
public static class PosDeviceSession
{
    public const string CookieName = "POS_DEVICE_KEY";
    public const string IdClaim = "gaoapp_pos_device_id";
    public const string StampClaim = "gaoapp_pos_device_stamp_v1";
    public const string ManagementClaim = "gaoapp_terminal_management";

    public static int? DeviceId(ClaimsPrincipal? principal)
        => int.TryParse(principal?.FindFirst(IdClaim)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;

    public static string CreateStamp(POSTerminalDevice device)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            device.Id, device.StoreId, device.TerminalId, device.DeviceKey, device.RowVersion
        });
        try { return Convert.ToHexString(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static bool Matches(ClaimsPrincipal principal, POSTerminalDevice device)
        => device.IsActive && !device.IsDeleted && DeviceId(principal) == device.Id &&
            AuthSessionStamp.Matches(principal.FindFirst(StampClaim)?.Value, CreateStamp(device));
}
