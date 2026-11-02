using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Application.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Security;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GaoApp.Tests.Storefront;

[CollectionDefinition("StorefrontWeb09", DisableParallelization = true)]
public sealed class Web09Collection : ICollectionFixture<Web01Fixture> { }

internal static class Web09Support
{
    internal static string Root([CallerFilePath] string source = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../.."));
    internal static string Artifact(string relative)
    {
        var root = Path.GetFullPath(Path.Combine(Root(), ".artifacts/storefront-rollout/WEB09/tests"));
        var path = Path.GetFullPath(Path.Combine(root, relative));
        Assert.StartsWith(root + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); return path;
    }
    internal static object Owner([CallerFilePath] string source = "")
    {
        using var process = Process.GetCurrentProcess();
        return new { ProcessId = process.Id, StartedUtc = process.StartTime.ToUniversalTime(), Executable = Environment.ProcessPath,
            Source = source, SourceSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) };
    }
    internal static string Runtime(string purpose)
    {
        var path = Artifact("runtime/" + Guid.NewGuid().ToString("N")); NoReparse(path);
        Assert.False(Directory.Exists(path)); Assert.False(File.Exists(path));
        var metadata = new { AtUtc = DateTime.UtcNow, Path = path, InitiallyAbsent = true, Purpose = purpose, Owner = Owner(),
            RetainArtifacts = true, NoPrivateKeyCopyOrManualCleanup = true };
        File.WriteAllText(Artifact("ownership/" + Path.GetFileName(path) + "-before.json"), JsonSerializer.Serialize(metadata));
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "owned-root.json"), JsonSerializer.Serialize(metadata)); return path;
    }
    internal static void NoReparse(string path)
    {
        path = Path.GetFullPath(path); Assert.StartsWith(Root() + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase);
        for (var part = path; !string.IsNullOrEmpty(part) && part.Length >= Root().Length; part = Path.GetDirectoryName(part)!)
            if (File.Exists(part) || Directory.Exists(part)) Assert.False((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0);
    }
    internal static string DirectoryBefore(string root, string name, string purpose)
    {
        Assert.Equal(Path.GetFileName(name), name);
        var path = Path.GetFullPath(Path.Combine(root, name)); NoReparse(path);
        Assert.False(Directory.Exists(path)); Assert.False(File.Exists(path));
        File.WriteAllText(Artifact("ownership/" + Path.GetFileName(root) + "-" + name + "-before.json"),
            JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow, Path = path, InitiallyAbsent = true, Purpose = purpose, Owner = Owner() }));
        Directory.CreateDirectory(path); return path;
    }
    internal static IConfiguration Policy(bool activation = true, string? subdomain = null) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["StorefrontRelease:AllowSettingsActivation"] = activation.ToString(),
            ["StorefrontHost:Host"] = "localhost", ["StorefrontHost:StoreSubdomain"] = subdomain }).Build();
    internal static string SettingsData(StorefrontSettings value, bool includeControl = true)
    {
        var excluded = includeControl ? Array.Empty<string>() : new[] { "IsEnabled", "Revision", "RowVersion", "UpdatedAtUtc", "UpdatedBy" };
        return JsonSerializer.Serialize(typeof(StorefrontSettings).GetProperties().Where(x => !excluded.Contains(x.Name))
            .OrderBy(x => x.Name).ToDictionary(x => x.Name, x => x.GetValue(value)));
    }
}

// Positive activation is an actual authorized service command. Neither this
// factory nor the WEB09 tests raw-enable the store to manufacture a positive.
internal sealed record Web09Case(Web01Case Seed, Web04Case Checkout)
{
    internal string? BoundSubdomain { get; init; }
    internal int StoreId => Seed.StoreId;
    internal string Directory => Checkout.Directory;
    internal AppDbContext Context() => Seed.Context();
    internal static async Task<Web09Case> CreateAsync(Web01Fixture fixture)
    {
        var seed = await fixture.CaseAsync(); await seed.SaveDefaultsAsync(); var pack = await seed.PackAsync();
        var root = Web09Support.Runtime("WEB09 independent synthetic COD configuration, operator activation and protected-key recovery; not owner acceptance");
        var keys = Web09Support.DirectoryBefore(root, "keys", "New WEB09 ring retained in place, no old ring/private-key copy");
        var web = new Web04Case(seed, pack.Conversion, pack.Unit, root, Path.Combine(root, "synthetic-addresses.json"), keys, new());
        await web.WriteAddressAsync("WEB09-ISOLATED-SYNTHETIC");
        await using (var db = seed.Context())
        {
            var variant = await db.ProductVariants.SingleAsync(x => x.Id == seed.VariantId); variant.Price = 20000m; variant.WholesalePrice = 1m;
            var conversion = await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.ConversionId); conversion.Price = 25000m; conversion.IsBaseUnit = true;
            db.InventoryBalances.Add(new() { StoreId = seed.StoreId, WarehouseId = seed.WarehouseId, ProductVariantId = seed.VariantId, OnHandQty = 100m });
            await db.SaveChangesAsync(); await seed.Products(db).SaveAsync(seed.ProductId, seed.Publication(pack: pack.Conversion));
        }
        await using (var db = seed.Context()) await seed.FullService(db).SaveZoneAsync(await seed.ZoneAsync("LOCAL", "01"));
        var c = new Web09Case(seed, web) { BoundSubdomain = await seed.SubdomainAsync() }; await c.GrantAsync(StorefrontPermissions.ViewSettings, StorefrontPermissions.ManageSettings);
        File.WriteAllText(Path.Combine(root, "fixture-lineage.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow,
            Owner = Web09Support.Owner(), Database = new SqlConnectionStringBuilder(seed.Database.ConnectionString).InitialCatalog,
            seed.StoreId, seed.UserId, seed.ProductId, seed.VariantId, KeyRoot = keys, PublicDefaultsUnchanged = true, PositiveActivationNotSeeded = true }));
        return c;
    }
    internal async Task GrantAsync(params string[] codes)
    {
        await using var db = Context(); var roleId = await db.UserInStores.Where(x => x.UserId == Seed.UserId).Select(x => x.RoleId).SingleAsync();
        var permissionIds = await db.Permissions.Where(x => codes.Contains(x.Code)).Select(x => x.Id).ToListAsync(); Assert.Equal(codes.Length, permissionIds.Count);
        foreach (var id in permissionIds)
            if (!await db.RolePermissions.AnyAsync(x => x.RoleId == roleId && x.PermissionId == id)) db.RolePermissions.Add(new() { RoleId = roleId, PermissionId = id });
        await db.SaveChangesAsync();
    }
    internal async Task RevokeAsync(string code)
    {
        await using var db = Context(); var role = await db.UserInStores.Where(x => x.UserId == Seed.UserId).Select(x => x.RoleId).SingleAsync();
        var id = await db.Permissions.Where(x => x.Code == code).Select(x => x.Id).SingleAsync();
        await db.RolePermissions.Where(x => x.RoleId == role && x.PermissionId == id).ExecuteDeleteAsync();
    }
    internal StorefrontSalesControlService Sales(AppDbContext db, bool activation = true)
        => new(db, new Web01Actor(Seed.UserId), new CurrentStorePermissionService(new UserInStoreRepository(db)), Web09Support.Policy(activation, BoundSubdomain), Checkout.Clock);
    internal async Task<StorefrontSalesControlRequest> CommandAsync(string reason = "WEB09 owned prepublication rehearsal only")
    {
        await using var db = Context(); var settings = await db.StorefrontSettings.AsNoTracking().SingleAsync();
        Assert.Equal(8, settings.RowVersion.Length); return new() { Version = Convert.ToBase64String(settings.RowVersion), Reason = reason };
    }
    internal async Task<StorefrontSalesControlState> ActivateAsync()
    { var request = await CommandAsync(); await using var db = Context(); return await Sales(db).ActivateAsync(request); }
    internal async Task<StorefrontSalesControlState> StopAsync(bool activationPolicy = true)
    { var request = await CommandAsync(); await using var db = Context(); return await Sales(db, activationPolicy).StopNewOrdersAsync(request); }
    internal IDataProtectionProvider Protection()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WEB09 protected release rehearsal currently targets Windows only.");
        StorefrontTestResourceMetadata.Provider(Checkout.KeysDirectory, "GaoApp.Storefront");
        return DataProtectionProvider.Create(new DirectoryInfo(Checkout.KeysDirectory), b =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WEB09 protected release rehearsal currently targets Windows only.");
            b.SetApplicationName("GaoApp.Storefront").ProtectKeysWithDpapi();
        });
    }
    internal StorefrontCheckoutService Commerce(AppDbContext db) => Checkout.Service(db, Protection());
    internal async Task<StorefrontQuoteDto> QuoteAsync()
    { await using var db = Context(); return await Commerce(db).QuoteAsync(Checkout.Request()); }
    internal async Task<StorefrontPlacementResult> PlaceAsync(StorefrontPlaceOrderRequest request)
    { await using var db = Context(); return await Commerce(db).PlaceAsync(request); }
}
