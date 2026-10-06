using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace GaoApp.Web.Services.StoreMonitor;

// A browser can report its own interaction state only for an authorized, server-rendered page.
// Actor, store, module and server-validated work identity are protected; input values are never accepted.
public sealed class StoreActivityTicket(IDataProtectionProvider protection, TimeProvider time)
{
    public sealed record Ticket(int StoreId, int UserId, string Module, DateTimeOffset IssuedAtUtc,
        string? WorkKey = null, string? Document = null);
    private readonly IDataProtector protector = protection.CreateProtector("GaoApp.StoreActivity.Page.v1");
    public string Issue(int storeId, int userId, string module, string? workKey = null, string? document = null) =>
        protector.Protect(JsonSerializer.Serialize(new Ticket(storeId, userId, module, time.GetUtcNow(), workKey, document)));
    public Ticket? Read(string value, int storeId, ClaimsPrincipal user)
    {
        if (value.Length > 2048 || user.Identity?.IsAuthenticated != true ||
            !int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return null;
        try
        {
            var ticket = JsonSerializer.Deserialize<Ticket>(protector.Unprotect(value));
            return ticket is not null && ticket.StoreId == storeId && ticket.UserId == userId &&
                StoreActivityCatalog.Modules.Contains(ticket.Module) && ticket.IssuedAtUtc <= time.GetUtcNow() &&
                time.GetUtcNow() - ticket.IssuedAtUtc <= TimeSpan.FromHours(12) ? ticket : null;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException or FormatException) { return null; }
    }
    public static string Person(ClaimsPrincipal user) =>
        user.FindFirstValue("full_name") is { Length: > 0 } fullName ? fullName : user.Identity?.Name ?? "Nhân viên";
    public static string? Terminal(ClaimsPrincipal user) => user.FindFirstValue("terminal_code");
}
