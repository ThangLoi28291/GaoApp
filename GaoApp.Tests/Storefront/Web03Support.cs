using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[CollectionDefinition("StorefrontWeb03", DisableParallelization = true)]
public sealed class Web03Collection : ICollectionFixture<Web01Fixture> { }

internal sealed record Web03Product(int ProductId, int VariantId, int UnitId, string Name);

internal static class Web03Support
{
    internal static string Root([System.Runtime.CompilerServices.CallerFilePath] string source = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../.."));
    internal static string Artifact(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(Root(), ".artifacts/storefront-rollout/WEB03/tests", relative));
        StorefrontTestResourceMetadata.BeforeDirectory(Path.GetDirectoryName(path)!, "WEB03 artifact parent");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); return path;
    }
    internal static async Task<Web03Product> AddProductAsync(this Web01Case c, string name, string group = "Gia dụng", int order = 0, decimal price = 25000)
    {
        await using var db = c.Context(); var template = await db.Products.AsNoTracking().SingleAsync(x => x.Id == c.ProductId);
        var product = new Product { StoreId = c.StoreId, Name = name, Alias = "web03-" + Guid.NewGuid().ToString("N"), CategoryId = template.CategoryId,
            SupplierId = template.SupplierId, BaseUnitId = c.UnitId, BasePrice = price, IsActive = true, IsSellable = true };
        db.Products.Add(product); await db.SaveChangesAsync();
        var variant = new ProductVariant { StoreId = c.StoreId, ProductId = product.Id, Sku = "W03-" + Guid.NewGuid().ToString("N"),
            ProductVariantName = "Lựa chọn tiêu chuẩn", Price = price, CostPrice = 9876, IsActive = true };
        db.ProductVariants.Add(variant); await db.SaveChangesAsync();
        db.ProductUnitConversions.Add(new() { StoreId = c.StoreId, ProductVariantId = variant.Id, UnitId = c.UnitId, Factor = 1, Price = price, IsBaseUnit = true });
        await db.SaveChangesAsync();
        await c.Products(db).SaveAsync(product.Id, new() { IsPublished = true, DisplayName = name, DisplayGroup = group, SortOrder = order,
            Description = "WEB03 mô tả thật từ SQL. <script>private-script-marker</script>",
            Variants = [new() { VariantId = variant.Id, IsPublished = true, BaseUnitIsPublished = true, BaseUnitWeightKg = 0.25m }] });
        return new(product.Id, variant.Id, c.UnitId, name);
    }
    internal static async Task<string> SubdomainAsync(this Web01Case c)
    { await using var db = c.Context(); return await db.Stores.Where(x => x.Id == c.StoreId).Select(x => x.SubDomainNormalized).SingleAsync(); }

    internal static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aMZcAAAAASUVORK5CYII=");
    internal static async Task<string> UploadsAsync(this Web01Case c, params (int AssetId, string StoragePath, byte[] Bytes)[] files)
    {
        var root = Artifact("runtime/" + Guid.NewGuid().ToString("N") + "/uploads"); StorefrontTestResourceMetadata.BeforeDirectory(root, "WEB03 fixture uploads"); Directory.CreateDirectory(root);
        foreach (var (_, storage, bytes) in files)
        {
            Assert.StartsWith("uploads/", storage); var full = Path.GetFullPath(Path.Combine(root, storage[8..].Replace('/', Path.DirectorySeparatorChar)));
            Assert.StartsWith(root + Path.DirectorySeparatorChar, full); Directory.CreateDirectory(Path.GetDirectoryName(full)!); await File.WriteAllBytesAsync(full, bytes);
        }
        return root;
    }
}

internal sealed class Web03PublicProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly ConcurrentQueue<string> output = new();
    private readonly string log;
    internal HttpClient Http { get; }
    internal string BaseUrl => Http.BaseAddress!.ToString().TrimEnd('/');
    private Web03PublicProcess(Process process, int port, string log)
    {
        this.process = process; this.log = log;
        Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); };
    }
    internal static async Task<Web03PublicProcess> StartAsync(Web01Case c, string? uploads = null, string environment = "Development", bool preview = true,
        string host = "127.0.0.1", string? subdomain = null, string bind = "127.0.0.1", int? fixedPort = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = fixedPort ?? ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var runtime = Web03Support.Artifact("runtime/" + Guid.NewGuid().ToString("N")); StorefrontTestResourceMetadata.BeforeDirectory(runtime, "WEB03 public host runtime"); Directory.CreateDirectory(runtime);
        var dll = Environment.GetEnvironmentVariable("GAOAPP_WEB03_PUBLIC_DLL") ?? Path.Combine(Web03Support.Root(), ".artifacts/storefront-rollout/build/bin/GaoApp.Storefront.Web/debug/GaoApp.Storefront.Web.dll");
        Assert.True(File.Exists(dll), "Root must build and freeze the separate public host before WEB03 test execution.");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = runtime, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.GetFullPath(dll)); start.ArgumentList.Add("--urls"); start.ArgumentList.Add($"http://{bind}:{port}");
        start.ArgumentList.Add("--contentRoot"); start.ArgumentList.Add(Path.Combine(Web03Support.Root(), "GaoApp.Storefront.Web"));
        start.Environment["ASPNETCORE_ENVIRONMENT"] = environment; start.Environment["DOTNET_ENVIRONMENT"] = environment;
        start.Environment["ConnectionStrings__DefaultConnection"] = c.Database.ConnectionString;
        start.Environment["StorefrontHost__Host"] = host; start.Environment["StorefrontHost__StoreSubdomain"] = subdomain ?? await c.SubdomainAsync();
        start.Environment["StorefrontHost__PreviewEnabled"] = preview.ToString(); start.Environment["StorefrontHost__UploadsRoot"] = uploads ?? Path.Combine(runtime, "uploads");
        // WEB04 moves this boundary from a placeholder to a real form. Keep
        // the WEB03 regression read-only while supplying isolated readiness.
        var keys = Path.Combine(runtime, "regression-private-keys"); StorefrontTestResourceMetadata.BeforeDirectory(keys, "WEB03 public key root"); Directory.CreateDirectory(keys);
        var addresses = Path.Combine(runtime, "regression-synthetic-addresses.json");
        await File.WriteAllTextAsync(addresses, """{"version":"WEB03-REGRESSION-SYNTHETIC","source":"Isolated regression fixture only","provinces":[{"code":"01","name":"Tỉnh fixture","wards":[{"code":"01001","name":"Phường fixture"}]}]}""");
        start.Environment["StorefrontHost__KeyRingPath"] = keys; start.Environment["StorefrontAddressCatalog__Path"] = addresses;
        var process = new Process { StartInfo = start }; var fixture = new Web03PublicProcess(process, port, Path.Combine(runtime, "public-host.log"));
        try
        {
            Assert.True(process.Start()); StorefrontTestResourceMetadata.Producer(process, keys, new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(c.Database.ConnectionString).InitialCatalog); process.BeginOutputReadLine(); process.BeginErrorReadLine(); var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(35))
            {
                if (process.HasExited) throw new InvalidOperationException("Public host exited: " + string.Join('\n', fixture.output));
                try { using var health = await fixture.Http.GetAsync("/health"); if (health.IsSuccessStatusCode) return fixture; } catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            throw new TimeoutException("Public host not healthy: " + string.Join('\n', fixture.output));
        }
        catch { await fixture.DisposeAsync(); throw; }
    }
    internal async Task<string> CsrfAsync()
    {
        var html = await Http.GetStringAsync("/shop/cart");
        var match = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Actual public Razor must issue a CSRF token."); return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    public async ValueTask DisposeAsync()
    {
        Http.Dispose(); if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        await File.WriteAllLinesAsync(log, output); process.Dispose();
    }
}
