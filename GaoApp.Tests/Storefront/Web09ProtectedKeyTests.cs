using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb09"), Trait("Category", "StorefrontWeb09")]
public sealed class Web09ProtectedKeyTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Existing_plaintext_framework_ring_fails_closed_even_with_a_valid_protected_new_key_without_adoption_or_rewrite(bool addValidProtectedKey)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WEB09 protected release vectors are Windows only.");
        var c = await Web09Case.CreateAsync(fixture); await c.ActivateAsync();
        // New synthetic negative material created through normal framework
        // generation. No old XML or private contents are parsed/copied/edited;
        // the metadata helper reads only file hashes and lengths.
        var provider = DataProtectionProvider.Create(new DirectoryInfo(c.Checkout.KeysDirectory), b => b.SetApplicationName("GaoApp.Storefront"));
        _ = provider.CreateProtector("WEB09 new plaintext fixture negative").Protect("synthetic non-secret input");
        if (addValidProtectedKey)
        {
            using var services = new ServiceCollection().AddDataProtection().SetApplicationName("GaoApp.Storefront")
                .PersistKeysToFileSystem(new DirectoryInfo(c.Checkout.KeysDirectory)).ProtectKeysWithDpapi().Services.BuildServiceProvider();
            var keys = services.GetRequiredService<IKeyManager>(); Assert.Single(keys.GetAllKeys());
            keys.CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90)); Assert.Equal(2, keys.GetAllKeys().Count);
        }
        var before = await Metadata(c.Checkout.KeysDirectory); Assert.NotEqual("[]", before); var counts = await c.Checkout.CountsAsync();
        await using (var host = await Web09PublicProcess.StartAsync(c))
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var readiness = await host.Http.GetAsync("/health/ready"); Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
                using var state = JsonDocument.Parse(await readiness.Content.ReadAsStringAsync());
                Assert.False(state.RootElement.GetProperty("runtimeReady").GetBoolean()); Assert.False(state.RootElement.GetProperty("acceptingNewOrders").GetBoolean());
                Assert.Contains("KEYRING_UNAVAILABLE", state.RootElement.ToString());
                using var route = await host.Http.GetAsync("/shop/products"); Assert.Equal(HttpStatusCode.ServiceUnavailable, route.StatusCode);
                Assert.Equal(before, await Metadata(c.Checkout.KeysDirectory));
            }
        }
        Assert.Equal(before, await Metadata(c.Checkout.KeysDirectory)); Assert.Equal(counts, await c.Checkout.CountsAsync());
        await File.WriteAllTextAsync(Path.Combine(c.Directory, "plaintext-key-negative-metadata.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow,
            Metadata = before, SyntheticNewFrameworkMaterial = true, ActualPrivateXmlParsedCopiedOrExported = false, AdoptedOrRegenerated = false }));
    }

    [Fact]
    public async Task Eligible_empty_owned_ring_bootstraps_normal_protected_key_and_same_ring_host_restart_keeps_runtime_ready()
    {
        var c = await Web09Case.CreateAsync(fixture); await c.ActivateAsync(); Assert.Empty(Directory.EnumerateFiles(c.Checkout.KeysDirectory, "*.xml"));
        await using (var host = await Web09PublicProcess.StartAsync(c))
        {
            using var ready = await host.Http.GetAsync("/health/ready"); Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            using var csrf = await host.Http.GetAsync("/shop/orders"); Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        }
        var first = await Metadata(c.Checkout.KeysDirectory); Assert.NotEqual("[]", first);
        await using (var restarted = await Web09PublicProcess.StartAsync(c))
        {
            using var ready = await restarted.Http.GetAsync("/health/ready"); Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            Assert.Equal(first, await Metadata(c.Checkout.KeysDirectory));
        }
    }

    private static async Task<string> Metadata(string root)
    {
        Web09Support.NoReparse(root); var rows = new List<object>();
        foreach (var path in Directory.EnumerateFiles(root, "*.xml", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
        {
            Web09Support.NoReparse(path); await using var stream = File.OpenRead(path);
            rows.Add(new { Path = Path.GetFullPath(path), Bytes = stream.Length, SHA256 = Convert.ToHexString(await SHA256.HashDataAsync(stream)) });
        }
        return JsonSerializer.Serialize(rows); // Only hashes/paths/lengths, never XML contents.
    }
}
