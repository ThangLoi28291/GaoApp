using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[CollectionDefinition("StorefrontWeb08", DisableParallelization = true)]
public sealed class Web08Collection : ICollectionFixture<Web08Fixture> { }

public sealed class Web08Fixture : IAsyncLifetime
{
    internal Web01Fixture Shared { get; } = new();
    public async Task InitializeAsync()
    {
        Web08Support.Record("fixture-before-sql-" + Guid.NewGuid().ToString("N"), new
        {
            Purpose = "WEB08 actual isolated SQL fixture, no Main or real store",
            Database = new SqlConnectionStringBuilder(Shared.Database.ConnectionString).InitialCatalog,
            Owner = Web08Support.Owner()
        });
        await Shared.InitializeAsync();
    }
    public Task DisposeAsync() => Shared.DisposeAsync();
}

internal static class Web08Support
{
    internal static string Root([CallerFilePath] string source = "")
        => Directory.GetParent(Path.GetDirectoryName(source)!)!.Parent!.FullName;
    internal static string Artifact(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(Root(), ".artifacts/storefront-rollout/WEB08/tests", relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); return path;
    }
    internal static object Owner([CallerFilePath] string source = "")
    {
        using var process = Process.GetCurrentProcess();
        return new { ProcessId = Environment.ProcessId, ProcessStartedUtc = process.StartTime.ToUniversalTime(),
            Executable = Environment.ProcessPath, Source = source, SourceSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) };
    }
    internal static void Record(string name, object value)
        => File.WriteAllText(Artifact("ownership/" + name + ".json"), JsonSerializer.Serialize(value));
    internal static void NoReparse(string path)
    {
        path = Path.GetFullPath(path); Assert.StartsWith(Root() + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase);
        for (var part = path; !string.IsNullOrEmpty(part) && part.Length >= Root().Length; part = Path.GetDirectoryName(part)!)
            if (Directory.Exists(part) || File.Exists(part)) Assert.False((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0);
    }
    internal static string NewDirectory(string purpose)
    {
        var id = Guid.NewGuid().ToString("N"); var path = Artifact("runtime/" + id); NoReparse(path);
        Assert.False(Directory.Exists(path)); Assert.False(File.Exists(path));
        var before = Artifact("ownership/" + id + "-before-create.json");
        File.WriteAllText(before, JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow, Path = path, InitiallyAbsent = true, Purpose = purpose, Owner = Owner() }));
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "owned-resource-root.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow, Path = path, Before = before,
            BeforeSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(before))), Purpose = purpose, Owner = Owner(), NoPrivateBytesCopied = true }));
        return path;
    }
    internal static string ChildDirectory(string root, string name)
    {
        Assert.DoesNotContain("..", name); Assert.False(Path.IsPathRooted(name));
        var path = Path.GetFullPath(Path.Combine(root, name)); Assert.StartsWith(root + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase); NoReparse(path);
        var existed = Directory.Exists(path);
        Record(Path.GetFileName(root) + "-child-" + name.Replace('/', '-').Replace('\\', '-'), new { AtUtc = DateTime.UtcNow, Path = path, InitiallyAbsent = !existed,
            ParentRoot = root, Owner = Owner(), Purpose = name });
        Directory.CreateDirectory(path); return path;
    }
}

internal sealed record Web08Case(Web04Case Web, string Directory, string Uploads, int AlternateVariantId, int ImageId, int GlbId, int GltfId)
{
    internal Web01Case Seed => Web.Seed;
    internal AppDbContext Context() => Seed.Context();
    internal int ProductId => Seed.ProductId;
    internal static async Task<Web08Case> CreateAsync(Web08Fixture fixture, bool models = true)
    {
        var directory = Web08Support.NewDirectory("WEB08 fixture product; genuine image/glTF bytes; no merchant asset claim");
        var keys = Web08Support.ChildDirectory(directory, "guest-keys"); var uploads = Web08Support.ChildDirectory(directory, "uploads");
        var seed = await fixture.Shared.CaseAsync(); var pack = await seed.PackAsync(price: 275000m); await seed.EnableSyntheticCatalogAsync();
        var web = new Web04Case(seed, pack.Conversion, pack.Unit, directory, Path.Combine(directory, "synthetic-addresses.json"), keys, new());
        await web.WriteAddressAsync("SYNTHETIC-WEB08-V1");
        int alternativeId;
        await using (var db = seed.Context())
        {
            var product = await db.Products.SingleAsync(x => x.Id == seed.ProductId); product.BasePrice = 25000;
            var primary = await db.ProductVariants.SingleAsync(x => x.Id == seed.VariantId); primary.Price = 25000; primary.WholesalePrice = 1;
            primary.Sku = "W08-PRIMARY-" + Guid.NewGuid().ToString("N"); primary.ProductVariantName = "Biến thể gốc — mô hình chỉ ở cấp sản phẩm";
            var conversion = await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.ConversionId); conversion.Price = 25000; conversion.IsBaseUnit = true;
            var alternate = new ProductVariant { StoreId = seed.StoreId, ProductId = seed.ProductId, Sku = "W08-ALT-" + Guid.NewGuid().ToString("N"),
                ProductVariantName = "Lựa chọn khác có giá riêng", Price = 35000, WholesalePrice = 1, IsActive = true };
            db.ProductVariants.Add(alternate); await db.SaveChangesAsync(); alternativeId = alternate.Id;
            db.ProductUnitConversions.Add(new() { StoreId = seed.StoreId, ProductVariantId = alternate.Id, UnitId = seed.UnitId, Factor = 1, Price = 35000, IsBaseUnit = true });
            db.InventoryBalances.Add(new() { StoreId = seed.StoreId, WarehouseId = seed.WarehouseId, ProductVariantId = seed.VariantId, OnHandQty = 100 });
            await db.SaveChangesAsync();
        }
        var image = await Asset(seed, directory, uploads, "image.png", "image/png", Web08FixtureAssets.Png(640, 320));
        var glb = models ? await Asset(seed, directory, uploads, "fixture.glb", "model/gltf-binary", Web08FixtureAssets.Model()) : 0;
        var gltf = models ? await Asset(seed, directory, uploads, "fixture.gltf", "model/gltf+json", Web08FixtureAssets.Model(binary: false)) : 0;
        await using (var db = seed.Context())
        {
            var request = seed.Publication(pack: pack.Conversion); request.DisplayName = "WEB08 mô hình thử nghiệm — chọn đúng biến thể, đơn vị và giá; không phải hàng thật";
            request.Description = "Fixture sở hữu riêng: ảnh và hình học glTF thật để kiểm tra pipeline, không phải nội dung đã được cửa hàng duyệt.";
            request.Variants.Add(new() { VariantId = alternativeId, IsPublished = true, BaseUnitIsPublished = true, BaseUnitWeightKg = .125m });
            request.Media = [new() { MediaAssetId = image, Kind = StorefrontMediaKind.Image, AltText = request.DisplayName, SortOrder = 0 }];
            if (models)
            {
                request.Media.Add(new() { MediaAssetId = glb, Kind = StorefrontMediaKind.Model3D, AltText = "GLB fixture bất đối xứng — cấp sản phẩm", SortOrder = 1 });
                request.Media.Add(new() { MediaAssetId = gltf, Kind = StorefrontMediaKind.Model3D, AltText = "glTF nhúng fixture — cấp sản phẩm", SortOrder = 2 });
            }
            await seed.Products(db).SaveAsync(seed.ProductId, request);
            var local = await seed.ZoneAsync("LOCAL", "01"); await seed.FullService(db).SaveZoneAsync(local);
            var national = await seed.ZoneAsync("NATIONAL", "", "", StorefrontDeliveryMethod.Nationwide); national.BaseFee = 30000; await seed.FullService(db).SaveZoneAsync(national);
        }
        var result = new Web08Case(web, directory, uploads, alternativeId, image, glb, gltf);
        File.WriteAllText(Path.Combine(directory, "owned-case-lineage.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow, Owner = Web08Support.Owner(),
            Database = new SqlConnectionStringBuilder(seed.Database.ConnectionString).InitialCatalog, seed.StoreId, seed.ProductId, seed.VariantId,
            AlternateVariantId = alternativeId, RuntimeRoot = directory, KeyRoot = keys, UploadsRoot = uploads, ImageId = image, GlbId = glb, GltfId = gltf,
            FixtureOnly = true, NoMerchantAssetOrBankAcceptanceClaim = true }));
        return result;
    }
    internal Task<int> AssetAsync(string name, string contentType, byte[] bytes) => Asset(Seed, Directory, Uploads, name, contentType, bytes);
    private static async Task<int> Asset(Web01Case seed, string directory, string uploads, string name, string contentType, byte[] bytes)
    {
        Assert.Equal(Path.GetFileName(name), name); var relative = "products/w08-" + Path.GetFileName(directory) + "/" + name;
        var full = Path.GetFullPath(Path.Combine(uploads, relative.Replace('/', Path.DirectorySeparatorChar))); Web08Support.NoReparse(full);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(full)!); Assert.False(File.Exists(full)); await File.WriteAllBytesAsync(full, bytes);
        await using var db = seed.Context(); var asset = new MediaAsset { StoreId = seed.StoreId, StoragePath = "uploads/" + relative,
            OriginalFileName = "WEB08-" + name, ContentType = contentType, SizeBytes = bytes.LongLength, IsTemp = false };
        db.MediaAssets.Add(asset); await db.SaveChangesAsync();
        Web08Support.Record(Path.GetFileName(directory) + "-asset-" + asset.Id, new { AtUtc = DateTime.UtcNow, Owner = Web08Support.Owner(), seed.StoreId,
            ProductId = seed.ProductId, asset.Id, Path = full, asset.StoragePath, asset.ContentType, asset.SizeBytes,
            SHA256 = Convert.ToHexString(SHA256.HashData(bytes)), FixtureOnly = true, PrivateBytesCopied = false }); return asset.Id;
    }
}
