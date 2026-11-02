using System.Security.Cryptography;
using System.Xml.Linq;
using GaoApp.Storefront.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb09"), Trait("Category", "StorefrontWeb09")]
public sealed class Web09KeyRepositoryTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("plaintext-old")]
    [InlineData("unsupported-old")]
    [InlineData("undecryptable-old")]
    [InlineData("duplicate-id")]
    public async Task Normal_framework_in_memory_old_key_fault_cannot_hide_behind_a_valid_new_key_or_regenerate_on_retry(string fault)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WEB09 framework DPAPI vectors are Windows only.");
        // These are NEW in-memory framework envelopes, never loaded from an
        // actual private file/ring. No private XML is read/exported/copied.
        var inner = new MemoryRepository();
        using (var old = NormalFramework(inner, protect: fault != "plaintext-old"))
            old.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(40));
        using (var current = NormalFramework(inner, protect: true))
            current.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));
        Assert.Equal(2, inner.Elements.Count);
        var original = inner.Elements[0];
        // Preserve the actual framework namespace; mutate only this new
        // in-memory witness, without guessing the production envelope XName.
        if (fault == "unsupported-old") original.Descendants().Single(x => x.Name.LocalName == "encryptedSecret").Attributes().Single(x => x.Name.LocalName == "decryptorType").Value = "WEB09.UnsupportedSyntheticDecryptor";
        if (fault == "undecryptable-old") original.Descendants().Single(x => x.Name.LocalName == "encryptedSecret").Descendants().Single(x => x.Name.LocalName == "value").Value = "AQID";
        if (fault == "duplicate-id") inner.Elements.Add(new XElement(original));
        var stores = inner.Stores; var repository = new StorefrontProtectedKeyRepository(inner);
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddDataProtection().SetApplicationName("GaoApp.Storefront").ProtectKeysWithDpapi()
            .AddKeyManagementOptions(o => { o.XmlRepository = repository; o.AutoGenerateKeys = false; });
        using var provider = services.BuildServiceProvider(); var c = await Web09Case.CreateAsync(fixture);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["StorefrontHost:ReleaseEnabled"] = "true", ["StorefrontHost:KeyProtection"] = "WindowsDpapiCurrentUser",
            ["StorefrontHost:Host"] = "localhost", ["StorefrontHost:StoreSubdomain"] = await c.Seed.SubdomainAsync() }).Build();
        var environment = new OwnedEnvironment();
        var gate = new StorefrontProtectedKeys(configuration, environment, new(true, c.Checkout.KeysDirectory), provider);
        await using var db = c.Context(); var before = await c.Checkout.CountsAsync();
        Assert.Equal("KEYRING_UNAVAILABLE", await gate.EnsureAsync(db, default));
        Assert.Equal(stores, inner.Stores);
        Assert.Equal("KEYRING_UNAVAILABLE", await gate.EnsureAsync(db, default)); Assert.Equal(stores, inner.Stores);
        Assert.Throws<CryptographicException>(() => repository.PermitValidatedWrites());
        Assert.Throws<CryptographicException>(() => repository.StoreElement(new XElement("revocation"), "WEB09 must not write"));
        Assert.Equal(stores, inner.Stores); Assert.Equal(before, await c.Checkout.CountsAsync());
    }
    private static ServiceProvider NormalFramework(IXmlRepository repository, bool protect)
    {
        var services = new ServiceCollection(); var builder = services.AddDataProtection().SetApplicationName("GaoApp.Storefront")
            .AddKeyManagementOptions(o => { o.XmlRepository = repository; o.AutoGenerateKeys = false; });
        if (protect)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI vectors require Windows.");
            builder.ProtectKeysWithDpapi();
        }
        return services.BuildServiceProvider();
    }
    private sealed class MemoryRepository : IXmlRepository
    {
        internal List<XElement> Elements { get; } = [];
        internal int Stores { get; private set; }
        public IReadOnlyCollection<XElement> GetAllElements() => Elements.Select(x => new XElement(x)).ToArray();
        public void StoreElement(XElement element, string friendlyName) { Elements.Add(new XElement(element)); Stores++; }
    }
    private sealed class OwnedEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "GaoApp.Storefront.Web";
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = Path.Combine(Web09Support.Root(), "GaoApp.Storefront.Web");
        public string WebRootPath { get; set; } = Path.Combine(Web09Support.Root(), "GaoApp.Storefront.Web/wwwroot");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
