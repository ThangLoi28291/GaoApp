using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GaoApp.Application.InvoiceNotifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.InvoiceNotifications;

/// <summary>Short-lived protected capabilities for GaoApp's original official PDFs. Tokens never contain storage paths.</summary>
public sealed class InvoiceOfficialDocumentLinks : IInvoiceOfficialDocumentLinkFactory
{
    private readonly IDataProtector protector;
    private readonly IOptionsMonitor<InvoiceOfficialDocumentLinkOptions> options;
    private readonly TimeProvider clock;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };
    public InvoiceOfficialDocumentLinks(IDataProtectionProvider protection, IOptionsMonitor<InvoiceOfficialDocumentLinkOptions> options, TimeProvider clock)
    { protector = protection.CreateProtector("GaoApp.InvoiceNotifications.OfficialDocumentLink", "v1"); this.options = options; this.clock = clock; }

    public Task<string> CreateAsync(InvoiceNotificationWorkItem item, InvoiceOfficialDocumentLinkMetadata pdf, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!TryBase(options.CurrentValue.PublicBaseUrl, out var root)) throw new InvoiceOfficialDocumentLinkNotConfiguredException();
        var s = item.Snapshot; var b = item.Binding; var artifact = s.Artifact;
        if (item.Kind is not ("OfficialLink" or "CorrectionLink") || s.Kind != item.Kind || item.SourceType != "Invoice" || s.SourceType != item.SourceType ||
            item.SourceId != s.SourceId || s.InvoiceId != item.SourceId || item.SourceVersion != s.SourceVersion || item.StoreId != s.StoreId || item.CustomerId != s.CustomerId ||
            b.StoreId != item.StoreId || b.CustomerId != item.CustomerId || b.RevokedAtUtc is not null || b.Phone != "0398946174" ||
            artifact is null || artifact.FileName != pdf.FileName || artifact.ContentType != pdf.ContentType || artifact.ByteLength != pdf.ByteLength ||
            !artifact.Sha256.Equals(pdf.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("OfficialLinkProofInvalid");
        var grant = new InvoiceOfficialDocumentLinkGrant(1, item.Id, item.StoreId, item.CustomerId, item.SourceType, item.SourceId, item.SourceVersion, item.Kind,
            b.Id, b.Version, root.AbsoluteUri, clock.GetUtcNow().AddHours(72), pdf);
        if (!Valid(grant)) throw new InvalidDataException("OfficialLinkProofInvalid");
        var token = protector.Protect(JsonSerializer.Serialize(grant, Json));
        return Task.FromResult(new Uri(root, "invoice-document?token=" + Uri.EscapeDataString(token)).AbsoluteUri);
    }

    public bool TryRead(string? token, out InvoiceOfficialDocumentLinkGrant grant)
    {
        grant = null!;
        if (token is null || token.Length is < 32 or > 8192 || !Regex.IsMatch(token, @"\A[A-Za-z0-9_-]+\z") || !TryBase(options.CurrentValue.PublicBaseUrl, out var root)) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<InvoiceOfficialDocumentLinkGrant>(protector.Unprotect(token), Json);
            if (parsed is null || !Valid(parsed) || parsed.Audience != root.AbsoluteUri || parsed.ExpiresAtUtc <= clock.GetUtcNow() ||
                parsed.ExpiresAtUtc > clock.GetUtcNow().AddHours(72).AddMinutes(1)) return false;
            grant = parsed; return true;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException or ArgumentException or InvalidDataException) { return false; }
    }
    public static bool ValidConfiguration(string? value) => string.IsNullOrWhiteSpace(value) || TryBase(value, out _);
    private static bool TryBase(string? value, out Uri root)
    {
        root = null!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(char.IsControl) || !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != "https" || parsed.UserInfo != "" || parsed.Query != "" || parsed.Fragment != "" || parsed.IsLoopback ||
            parsed.HostNameType != UriHostNameType.Dns || parsed.AbsolutePath.Contains("//", StringComparison.Ordinal)) return false;
        root = new Uri(parsed.AbsoluteUri.TrimEnd('/') + "/"); return true;
    }
    private static bool Valid(InvoiceOfficialDocumentLinkGrant grant)
    {
        if (grant.SchemaVersion != 1 || grant.NotificationId == Guid.Empty || grant.StoreId <= 0 || grant.CustomerId <= 0 || grant.SourceType != "Invoice" ||
            grant.SourceId <= 0 || grant.Kind is not ("OfficialLink" or "CorrectionLink") || grant.BindingId == Guid.Empty || grant.BindingVersion <= 0 ||
            grant.SourceVersion is null || !Regex.IsMatch(grant.SourceVersion, @"\A[0-9a-fA-F]{64}\z") || grant.Pdf is null ||
            grant.Pdf.ContentType != "application/pdf" || grant.Pdf.ByteLength is < 1 or > 20L * 1024 * 1024 || grant.Pdf.Sha256 is null ||
            !Regex.IsMatch(grant.Pdf.Sha256, @"\A[0-9a-fA-F]{64}\z") || grant.Pdf.FileName is null ||
            !Path.GetExtension(grant.Pdf.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return false;
        try { return InvoiceNotificationMediaPolicy.NormalizeFileName(grant.Pdf.FileName) == grant.Pdf.FileName; }
        catch (InvalidDataException) { return false; }
    }
}
