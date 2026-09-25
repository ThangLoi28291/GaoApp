using System.Net;
using System.Text;
using GaoApp.Application.Common.Options;
using GaoApp.Infrastructure.Storage;
using GaoApp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Security;

public sealed class HostStorageTests
{
    [Fact]
    public async Task Storage_uses_configured_root_and_interrupted_replace_preserves_original()
    {
        using var fixture = new HostFiles();
        var storage = new LocalFileStorageService(fixture.Paths, NullLogger<LocalFileStorageService>.Instance);
        await storage.SaveAsync(new MemoryStream(Encoding.UTF8.GetBytes("original")), "uploads/_temp/t/photo.png");
        await storage.MoveAsync("uploads/_temp/t/photo.png", "uploads/products/photo.png");
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(fixture.Uploads, "products/photo.png")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Environment.WebRootPath, "uploads")));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.SaveAsync(new MemoryStream(new byte[4096]), "uploads/products/photo.png", cts.Token));
        Assert.Equal("original", await File.ReadAllTextAsync(fixture.Paths.Resolve("uploads/products/photo.png")));
        Assert.Empty(Directory.GetFiles(fixture.Uploads, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("uploads/../keys/secret.png")]
    [InlineData("uploads/products/../../secret.png")]
    [InlineData("uploads/products/secret.png:stream")]
    [InlineData("uploads/products/secret. /image.png")]
    [InlineData("C:/secrets/file.png")]
    [InlineData("uploads\\products\\file.png")]
    public void Traversal_and_platform_aliases_are_rejected(string path)
    {
        using var fixture = new HostFiles();
        Assert.Throws<InvalidOperationException>(() => fixture.Paths.Resolve(path));
    }

    [Fact]
    public async Task Http_serves_persistent_and_legacy_media_but_never_private_uploads()
    {
        using var fixture = new HostFiles();
        await fixture.Write("products/photo.png", "external");
        await fixture.Write("display/video.mp4", "0123456789");
        await fixture.Write("legacy-data/images/Do Gia Dung/túi gạo.jpg", "migrated image");
        await fixture.Write("legacy-data/files/A+B.PNG", "migrated file");
        foreach (var file in new[] { "invoices/secret.png", "_temp/token/photo.png", "products/secret.pdf", "data/private.xml", "display/key.pfx", "products/script.svg", "legacy-data/private/secret.jpg", "legacy-data/images/secret.xml", "legacy-data/files/script.svg", "legacy-data/files/video.mp4" })
            await fixture.Write(file, "private");
        var legacy = Path.Combine(fixture.Environment.WebRootPath, "uploads/data");
        Directory.CreateDirectory(legacy);
        await File.WriteAllTextAsync(Path.Combine(legacy, "old.jpg"), "legacy");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = fixture.Environment.ContentRootPath, WebRootPath = fixture.Environment.WebRootPath });
        builder.Configuration["AllowedHosts"] = "*";
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.UsePublicUploads(app.Environment, fixture.Paths); app.UseStaticFiles();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            using var photo = await client.GetAsync("/uploads/products/photo.png");
            Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
            Assert.Equal("external", await photo.Content.ReadAsStringAsync());
            Assert.Equal("nosniff", photo.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("legacy", await client.GetStringAsync("/uploads/data/old.jpg"));
            Assert.Equal("migrated image", await client.GetStringAsync("/uploads/legacy-data/images/Do%20Gia%20Dung/t%C3%BAi%20g%E1%BA%A1o.jpg"));
            Assert.Equal("migrated file", await client.GetStringAsync("/uploads/legacy-data/files/A%2BB.PNG"));
            using var range = new HttpRequestMessage(HttpMethod.Get, "/uploads/display/video.mp4");
            range.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 3);
            using var video = await client.SendAsync(range);
            Assert.Equal(HttpStatusCode.PartialContent, video.StatusCode);
            Assert.Equal("0123", await video.Content.ReadAsStringAsync());
            foreach (var path in new[] { "invoices/secret.png", "_temp/token/photo.png", "products/secret.pdf", "data/private.xml", "display/key.pfx", "products/script.svg", "legacy-data/private/secret.jpg", "legacy-data/images/secret.xml", "legacy-data/files/script.svg", "legacy-data/files/video.mp4" })
            {
                using var response = await client.GetAsync("/uploads/" + path);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }
        }
        finally { await app.StopAsync(); }
    }
}

internal sealed class HostFiles : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GaoApp_HostTests_" + Guid.NewGuid().ToString("N"));
    public TestHostEnvironment Environment { get; }
    public string Uploads { get; }
    public UploadPathResolver Paths { get; }
    public HostFiles()
    {
        Environment = new TestHostEnvironment { ContentRootPath = Path.Combine(root, "publish"), WebRootPath = Path.Combine(root, "publish/wwwroot") };
        Uploads = Path.Combine(root, "persistent");
        Directory.CreateDirectory(Environment.WebRootPath); Directory.CreateDirectory(Uploads);
        Paths = new UploadPathResolver(Environment, Options.Create(new StorageOptions { UploadRoot = Uploads }));
    }
    public async Task Write(string relative, string text)
    {
        var path = Paths.Resolve("uploads/" + relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, text);
    }
    public void Dispose()
    {
        var full = Path.GetFullPath(root);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("GaoApp_HostTests_"))
            throw new InvalidOperationException("Unsafe test cleanup target.");
        Directory.Delete(full, true);
    }
}

internal sealed class TestHostEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Production";
    public string ApplicationName { get; set; } = "GaoApp.Tests";
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
