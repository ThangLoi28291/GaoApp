using System.Net;
using System.Security.Cryptography;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.InvoiceNotifications;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Services.InvoiceNotifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GaoApp.Web.Controllers;

[AllowAnonymous, Route("invoice-document"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), EnableRateLimiting("invoice-buyer-self-service")]
public sealed class InvoiceDocumentController(InvoiceOfficialDocumentLinks links, IInvoiceOfficialDocumentLinkAuthorizer authorizer,
    IBoundedInvoiceFileStorage files, TenantContext tenant) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? token, CancellationToken ct)
    {
        Headers();
        var access = await AccessAsync(token, ct);
        if (access is null) return NotFound();
        var href = new Uri(new Uri(access.Value.Grant.Audience), "invoice-document/download?token=" + Uri.EscapeDataString(token!)).AbsoluteUri;
        var expiry = access.Value.Grant.ExpiresAtUtc.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        // Standalone HTML: no layout, third-party resources, scripts, forms, automatic download or provider URL.
        return Content("<!doctype html><html lang=\"vi\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><meta name=\"referrer\" content=\"no-referrer\"><title>Xem hóa đơn</title></head><body><main><h1>Xem hóa đơn</h1><p>Bản PDF chính thức đã lưu tại GaoApp.</p><p>Liên kết hết hạn: " + WebUtility.HtmlEncode(expiry) + " (UTC+7).</p><p><a rel=\"noreferrer\" href=\"" + WebUtility.HtmlEncode(href) + "\">Tải hóa đơn PDF</a></p></main></body></html>", "text/html; charset=utf-8");
    }

    [HttpGet("download")]
    public async Task<IActionResult> Download([FromQuery] string? token, CancellationToken ct)
    {
        Headers();
        var access = await AccessAsync(token, ct);
        if (access is null) return NotFound();
        try
        {
            var artifact = access.Value.Access.Snapshot.Artifact!; var expected = access.Value.Grant.Pdf;
            var stored = await files.ReadBoundedAsync(artifact.StoragePath, 20L * 1024 * 1024, ct);
            if (stored is null || stored.Value.FileName != expected.FileName || stored.Value.ContentType != "application/pdf" ||
                stored.Value.Bytes.LongLength != expected.ByteLength || !stored.Value.Bytes.AsSpan().StartsWith("%PDF-"u8) ||
                !Convert.ToHexString(SHA256.HashData(stored.Value.Bytes)).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase)) return NotFound();
            // Revalidate after file I/O as well: a revocation/source change during materialization must deny access.
            var final = await AccessAsync(token, ct);
            if (final is null || final.Value.Grant != access.Value.Grant) return NotFound();
            return File(stored.Value.Bytes, "application/pdf", expected.FileName, enableRangeProcessing: false);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { return NotFound(); }
    }

    private async Task<(InvoiceOfficialDocumentLinkGrant Grant, InvoiceOfficialDocumentLinkAccess Access)?> AccessAsync(string? token, CancellationToken ct)
    {
        if (!links.TryRead(token, out var grant)) return null;
        var audience = new Uri(grant.Audience);
        if (!Request.IsHttps || !Request.Host.Host.Equals(audience.IdnHost, StringComparison.OrdinalIgnoreCase) || (Request.Host.Port ?? 443) != audience.Port ||
            (Request.PathBase.Value ?? "") != audience.AbsolutePath.TrimEnd('/') || tenant.StoreId is { } store && store != grant.StoreId) return null;
        tenant.SetStore(grant.StoreId, "invoice-document-link");
        var access = await authorizer.AuthorizeAsync(grant, ct);
        if (access is null || access.Binding.Id != grant.BindingId || access.Binding.Version != grant.BindingVersion || access.Binding.StoreId != grant.StoreId ||
            access.Binding.CustomerId != grant.CustomerId || access.Binding.RevokedAtUtc is not null || access.Binding.Phone != "0398946174" ||
            access.Snapshot.StoreId != grant.StoreId || access.Snapshot.CustomerId != grant.CustomerId || access.Snapshot.SourceType != grant.SourceType ||
            access.Snapshot.SourceId != grant.SourceId || access.Snapshot.SourceVersion != grant.SourceVersion || access.Snapshot.Kind != grant.Kind || access.Snapshot.UnsupportedReason is not null ||
            access.Snapshot.Artifact is not { } a || a.FileName != grant.Pdf.FileName || a.ContentType != grant.Pdf.ContentType || a.ByteLength != grant.Pdf.ByteLength ||
            !a.Sha256.Equals(grant.Pdf.Sha256, StringComparison.OrdinalIgnoreCase)) return null;
        return (grant, access);
    }
    private void Headers()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0"; Response.Headers.Pragma = "no-cache"; Response.Headers.Expires = "0";
        Response.Headers.XContentTypeOptions = "nosniff"; Response.Headers.XFrameOptions = "DENY";
        Response.Headers["Referrer-Policy"] = "no-referrer"; Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'; script-src 'none'; connect-src 'none'";
    }
}
