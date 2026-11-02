using System.Security.Cryptography;
using System.Text;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.InvoiceNotifications;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Controllers;
using GaoApp.Web.Services.InvoiceNotifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GaoApp.Web.Security;
using Microsoft.AspNetCore.RateLimiting;

namespace GaoApp.Tests.InvoiceNotifications;

public sealed class InvoiceOfficialDocumentLinkTests
{
    private readonly Clock clock = new();
    private readonly Monitor<InvoiceOfficialDocumentLinkOptions> options = new(new() { PublicBaseUrl = "https://invoice.example.test/" });
    private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();
    private readonly byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\n%%EOF\n");
    private readonly InvoiceNotificationWorkItem item;
    private readonly InvoiceOfficialDocumentLinkMetadata metadata;
    public InvoiceOfficialDocumentLinkTests()
    {
        metadata = new("official-synthetic.pdf", "application/pdf", pdf.Length, Convert.ToHexString(SHA256.HashData(pdf)));
        var binding = new InvoiceRecipientBindingSnapshot(Guid.NewGuid(), 7, 11, "zalo-synthetic", new string('c',64), Guid.NewGuid(), Guid.NewGuid(), 42, "sdk-v1:123", "0398946174", 1, clock.GetUtcNow().UtcDateTime, null);
        var snapshot = new InvoiceDocumentSnapshot { StoreId = 7, CustomerId = 11, SourceType = "Invoice", SourceId = 99, InvoiceId = 99, SourceVersion = new string('a',64), Kind = "OfficialLink", Status = "Issued", IssuedAtUtc = clock.GetUtcNow().UtcDateTime,
            Artifact = new("uploads/invoices/SYNTHETIC-private.pdf", metadata.FileName, metadata.ContentType, metadata.ByteLength, metadata.Sha256) };
        item = new(Guid.NewGuid(), Guid.NewGuid(), 7, 11, "Invoice", 99, snapshot.SourceVersion, "OfficialLink", Guid.NewGuid(), "gao-synthetic-link", "Dispatching", clock.GetUtcNow().UtcDateTime, binding, snapshot, null, null, 1);
    }
    private InvoiceOfficialDocumentLinks Links() => new(protection, options, clock);
    private async Task<(InvoiceOfficialDocumentLinks Links, string Token)> Token()
    {
        var links = Links(); var url = await links.CreateAsync(item, metadata, default);
        Assert.StartsWith("https://invoice.example.test/invoice-document?token=", url);
        return (links, Uri.UnescapeDataString(new Uri(url).Query[7..]));
    }
    private InvoiceDocumentController Controller(InvoiceOfficialDocumentLinks links, Authorizer authorizer, Files files, TenantContext? tenant = null)
    {
        var context = new DefaultHttpContext(); context.Request.Scheme = "https"; context.Request.Host = new HostString("invoice.example.test");
        return new(links, authorizer, files, tenant ?? new()) { ControllerContext = new() { HttpContext = context } };
    }
    private Authorizer Authorize() => new(new(item.Snapshot, item.Binding));
    private Files Storage() => new(pdf, metadata.ContentType, metadata.FileName);
    [Fact]
    public async Task Token_is_opaque_query_only_and_binds_document_recipient_version_metadata_audience_and_72_hours()
    {
        var (links, token) = await Token(); Assert.DoesNotContain("uploads", token); Assert.DoesNotContain("private", token); Assert.DoesNotContain(metadata.FileName, token);
        Assert.True(links.TryRead(token, out var grant)); Assert.Equal(item.Id, grant.NotificationId); Assert.Equal(item.Binding.Id, grant.BindingId); Assert.Equal(item.Binding.Version, grant.BindingVersion);
        Assert.Equal(item.StoreId, grant.StoreId); Assert.Equal(item.CustomerId, grant.CustomerId); Assert.Equal(item.SourceVersion, grant.SourceVersion); Assert.Equal(metadata, grant.Pdf); Assert.Equal(clock.GetUtcNow().AddHours(72), grant.ExpiresAtUtc);
        var plain = protection.CreateProtector("GaoApp.InvoiceNotifications.OfficialDocumentLink", "v1").Unprotect(token);
        Assert.DoesNotContain("storagePath", plain, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("storageKey", plain, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("ReservationCode", plain, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task Default_unconfigured_links_fail_explicitly_without_changing_document_features()
    {
        options.CurrentValue.PublicBaseUrl = "";
        await Assert.ThrowsAsync<InvoiceOfficialDocumentLinkNotConfiguredException>(() => Links().CreateAsync(item, metadata, default)); Assert.True(InvoiceOfficialDocumentLinks.ValidConfiguration(""));
    }
    [Theory][InlineData("http://invoice.example.test")][InlineData("https://user:secret@invoice.example.test")][InlineData("https://invoice.example.test/?token=test")][InlineData("https://invoice.example.test/#test")][InlineData("https://127.0.0.1")]
    public void Unsafe_public_hosts_urls_or_credentials_are_never_accepted(string url) => Assert.False(InvoiceOfficialDocumentLinks.ValidConfiguration(url));
    [Fact]
    public async Task Corrupt_wrong_purpose_expired_and_changed_audience_tokens_are_denied_before_core_access()
    {
        var (links, token) = await Token();
        Assert.False(links.TryRead(token[..^5] + "xxxxx", out _));
        var wrong = protection.CreateProtector("another-purpose").Protect("{}"); Assert.False(links.TryRead(wrong, out _));
        options.CurrentValue.PublicBaseUrl = "https://other.example.test/"; Assert.False(links.TryRead(token, out _));
        options.CurrentValue.PublicBaseUrl = "https://invoice.example.test/"; clock.Now = clock.Now.AddHours(72); Assert.False(links.TryRead(token, out _));
        var authorizer = Authorize(); Assert.IsType<NotFoundResult>(await Controller(links, authorizer, Storage()).Download(token, default)); Assert.Equal(0, authorizer.Calls);
    }
    [Fact]
    public async Task Original_pdf_metadata_must_match_the_work_item_before_a_token_can_be_created()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Links().CreateAsync(item, metadata with { Sha256 = new string('f',64) }, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => Links().CreateAsync(item with { CustomerId = 12 }, metadata, default));
    }
    [Fact]
    public async Task Anonymous_landing_is_standalone_no_store_no_referrer_and_has_only_a_GaoApp_download_link()
    {
        var (links, token) = await Token(); var controller = Controller(links, Authorize(), Storage());
        var page = Assert.IsType<ContentResult>(await controller.Index(token, default));
        Assert.Contains("Xem hóa đơn", page.Content); Assert.Contains("/invoice-document/download?token=", page.Content);
        Assert.DoesNotContain("<script", page.Content); Assert.DoesNotContain("<iframe", page.Content); Assert.DoesNotContain("<link", page.Content); Assert.DoesNotContain("<form", page.Content); Assert.DoesNotContain("uploads/", page.Content);
        Assert.Equal("no-referrer", controller.Response.Headers["Referrer-Policy"]); Assert.Contains("no-store", controller.Response.Headers.CacheControl.ToString()); Assert.Contains("default-src 'none'", controller.Response.Headers.ContentSecurityPolicy.ToString());
    }
    [Fact]
    public async Task Download_returns_exact_original_provider_bytes_without_ranges_or_public_storage_paths()
    {
        var (links, token) = await Token(); var auth = Authorize(); var storage = Storage(); var controller = Controller(links, auth, storage);
        var file = Assert.IsType<FileContentResult>(await controller.Download(token, default));
        Assert.Equal(pdf, file.FileContents); Assert.Equal(metadata.FileName, file.FileDownloadName); Assert.Equal("application/pdf", file.ContentType); Assert.False(file.EnableRangeProcessing); Assert.Equal(2, auth.Calls); Assert.Equal(1, storage.Reads); Assert.Equal(item.Snapshot.Artifact!.StoragePath, storage.Path);
    }
    [Theory][InlineData("revoked")][InlineData("customer")][InlineData("source")][InlineData("hash")][InlineData("fileName")]
    public async Task Current_recipient_or_source_mismatch_is_denied_before_reading_any_bytes(string fault)
    {
        var (links, token) = await Token(); var auth = Authorize(); var storage = Storage();
        if (fault == "revoked") auth.Access = auth.Access! with { Binding = item.Binding with { RevokedAtUtc = clock.Now.UtcDateTime } };
        if (fault == "customer") auth.Access = auth.Access! with { Snapshot = item.Snapshot with { CustomerId = 12 } };
        if (fault == "source") auth.Access = auth.Access! with { Snapshot = item.Snapshot with { SourceVersion = new string('b',64) } };
        if (fault == "hash") auth.Access = auth.Access! with { Snapshot = item.Snapshot with { Artifact = item.Snapshot.Artifact! with { Sha256 = new string('b',64) } } };
        if (fault == "fileName") auth.Access = auth.Access! with { Snapshot = item.Snapshot with { Artifact = item.Snapshot.Artifact! with { FileName = "foreign.pdf" } } };
        Assert.IsType<NotFoundResult>(await Controller(links, auth, storage).Download(token, default)); Assert.Equal(0, storage.Reads);
    }
    [Fact]
    public async Task Feature_disabled_or_binding_revoked_by_core_denies_landing_and_download_without_reading_storage()
    {
        var (links, token) = await Token(); var auth = Authorize(); auth.Access = null; var storage = Storage(); var controller = Controller(links, auth, storage);
        Assert.IsType<NotFoundResult>(await controller.Index(token, default)); Assert.IsType<NotFoundResult>(await controller.Download(token, default)); Assert.Equal(0, storage.Reads);
    }
    [Theory][InlineData("http")][InlineData("host")][InlineData("tenant")]
    public async Task Tokens_cannot_be_used_on_a_different_host_insecure_scheme_or_another_resolved_store(string fault)
    {
        var (links, token) = await Token(); var auth = Authorize(); var tenant = new TenantContext(); if (fault == "tenant") tenant.SetStore(99, "other");
        var controller = Controller(links, auth, Storage(), tenant); if (fault == "http") controller.Request.Scheme = "http"; if (fault == "host") controller.Request.Host = new("other.example.test");
        Assert.IsType<NotFoundResult>(await controller.Download(token, default)); Assert.Equal(0, auth.Calls);
    }
    [Fact]
    public async Task Original_file_changes_or_expiry_or_revocation_during_read_cannot_release_bytes()
    {
        var (links, token) = await Token(); var auth = Authorize(); var storage = Storage(); storage.Bytes = Encoding.ASCII.GetBytes("%PDF-1.4 DIFFERENT");
        Assert.IsType<NotFoundResult>(await Controller(links, auth, storage).Download(token, default));
        storage = Storage(); storage.AfterRead = () => auth.Access = null;
        Assert.IsType<NotFoundResult>(await Controller(links, auth, storage).Download(token, default));
        auth = Authorize(); storage = Storage(); storage.AfterRead = () => auth.Access = auth.Access! with { Snapshot = item.Snapshot with { SourceVersion = new string('b',64) } };
        Assert.IsType<NotFoundResult>(await Controller(links, auth, storage).Download(token, default));
        auth = Authorize(); storage = Storage(); storage.AfterRead = () => clock.Now = clock.Now.AddHours(73);
        Assert.IsType<NotFoundResult>(await Controller(links, auth, storage).Download(token, default));
    }
    [Fact]
    public async Task Anonymous_public_HTTP_landing_and_download_keep_strict_headers_after_real_OnStarting_middleware()
    {
        var (links, token) = await Token(); var auth = Authorize(); var storage = Storage();
        var directory = Path.Combine(Path.GetTempPath(), "GaoOfficialLinkHttpTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = directory, EnvironmentName = "Development" });
        builder.Configuration["AllowedHosts"] = "invoice.example.test"; builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(InvoiceDocumentController).Assembly);
        builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("invoice-buyer-self-service", limiter => { limiter.PermitLimit = 100; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; }));
        builder.Services.AddSingleton(links); builder.Services.AddSingleton<IInvoiceOfficialDocumentLinkAuthorizer>(auth); builder.Services.AddSingleton<IBoundedInvoiceFileStorage>(storage); builder.Services.AddScoped<TenantContext>();
        await using var app = builder.Build(); app.Urls.Add("http://127.0.0.1:0");
        // Synthetic local TLS termination. Production obtains Scheme through its trusted forwarded-header policy.
        app.Use((context, next) => { context.Request.Scheme = "https"; return next(context); });
        app.UseRouting(); app.UseMiddleware<SecurityResponseHeadersMiddleware>(); app.UseRateLimiter(); app.MapControllers(); await app.StartAsync();
        try
        {
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri(app.Urls.Single()) };
            http.DefaultRequestHeaders.Host = "invoice.example.test";
            using var landing = await http.GetAsync("invoice-document?token=" + Uri.EscapeDataString(token)); Assert.Equal(System.Net.HttpStatusCode.OK, landing.StatusCode);
            Assert.Equal("no-referrer", landing.Headers.GetValues("Referrer-Policy").Single()); Assert.Equal("DENY", landing.Headers.GetValues("X-Frame-Options").Single());
            Assert.Contains("default-src 'none'", landing.Headers.GetValues("Content-Security-Policy").Single()); Assert.True(landing.Headers.CacheControl!.NoStore);
            var html = await landing.Content.ReadAsStringAsync(); Assert.DoesNotContain("<script", html); Assert.DoesNotContain("<link", html); Assert.Contains("Tải hóa đơn PDF", html);
            using var download = await http.GetAsync("invoice-document/download?token=" + Uri.EscapeDataString(token)); Assert.Equal(System.Net.HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(pdf, await download.Content.ReadAsByteArrayAsync()); Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType); Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single()); Assert.True(download.Headers.CacheControl!.NoStore);
            var reads = storage.Reads; auth.Access = null;
            using var denied = await http.GetAsync("invoice-document/download?token=" + Uri.EscapeDataString(token)); Assert.Equal(System.Net.HttpStatusCode.NotFound, denied.StatusCode); Assert.Equal(reads, storage.Reads); Assert.True(denied.Headers.CacheControl!.NoStore);
        }
        finally { await app.StopAsync(); Directory.Delete(directory, true); }
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = new(2026,10,27,0,0,0,TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Monitor<T>(T value) : IOptionsMonitor<T> { public T CurrentValue => value; public T Get(string? name) => value; public IDisposable? OnChange(Action<T,string?> listener) => null; }
    private sealed class Authorizer(InvoiceOfficialDocumentLinkAccess? access) : IInvoiceOfficialDocumentLinkAuthorizer
    { public InvoiceOfficialDocumentLinkAccess? Access = access; public int Calls; public Task<InvoiceOfficialDocumentLinkAccess?> AuthorizeAsync(InvoiceOfficialDocumentLinkGrant grant, CancellationToken ct) { Calls++; return Task.FromResult(Access); } }
    private sealed class Files(byte[] bytes, string type, string name) : IBoundedInvoiceFileStorage
    {
        public byte[] Bytes = bytes; public int Reads; public string? Path; public Action? AfterRead;
        public Task<(byte[] Bytes,string ContentType,string FileName)?> ReadBoundedAsync(string storedPath,long maximumBytes,CancellationToken ct)
        { Reads++; Path=storedPath; AfterRead?.Invoke(); return Task.FromResult<(byte[],string,string)?>((Bytes,type,name)); }
    }
}
