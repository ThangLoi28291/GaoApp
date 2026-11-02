using System.Diagnostics;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class Ui24ReleaseAcceptanceTests
{
    [Fact]
    public async Task Verified_WebOnly_release_supports_HTTPS_ui_performance_and_byte_exact_rollback_without_schema_or_store_changes()
    {
        var root = FullApplicationFixture.SourceRoot();
        var before = Release("GAO_UI24_BASELINE_RELEASE");
        var candidate = Release("GAO_UI24_CANDIDATE_RELEASE");
        var output = Path.GetFullPath(Environment.GetEnvironmentVariable("GAO_UI24_RELEASE_OUTPUT")
            ?? Path.Combine(root, "TestResults", "mobile-ui-24-20261029", "release"));
        Assert.StartsWith(Path.Combine(root, "TestResults") + Path.DirectorySeparatorChar, output, StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(output);
        var baselineProof = Verify(before); var candidateProof = Verify(candidate);
        await using var app = new PublishedProductionSmokeTests.PublishedFixture(before);
        await app.Database.MigrateAsync();
        var seed = await app.Database.SeedInventoryCatalogAsync();
        int terminalId, conversionId;
        const string userName = "ui24_release_owner";
        var password = "Ui24!" + Guid.NewGuid().ToString("N") + "aA7";
        await using (var db = app.Database.CreateTenantContext(seed.StoreId))
        {
            var store = await db.Stores.SingleAsync(x => x.Id == seed.StoreId);
            store.SubDomain = "shop"; store.SubDomainNormalized = "SHOP";
            var terminal = new POSTerminal { StoreId = seed.StoreId, Code = "UI24", Name = "Quầy UI24" }; db.Add(terminal);
            var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == seed.ProductVariantId);
            var conversion = new ProductUnitConversion { StoreId = seed.StoreId, ProductVariantId = variant.Id, UnitId = variant.Product.BaseUnitId,
                Factor = 1, IsBaseUnit = true, IsDefaultForSale = true, Price = 20 };
            db.Add(conversion); await db.SaveChangesAsync(); terminalId = terminal.Id; conversionId = conversion.Id;
            db.Add(new ProductVariantUnitBarcode { StoreId = seed.StoreId, ProductUnitConversionId = conversionId, Barcode = "8934588063053", IsActive = true, IsPrimary = true });
            await GaoApp.Infrastructure.Data.Seed.SecuritySeedData.SeedPermissionsAsync(db);
            var role = new Role { StoreId = seed.StoreId, Code = "ADMIN", Name = "UI24 fixture owner" };
            var user = new User { UserName = userName, FullName = "UI24 fixture owner", PasswordHash = new PasswordHasher().Hash(password) };
            db.Add(new UserInStore { StoreId = seed.StoreId, User = user, Role = role });
            foreach (var id in await db.Permissions.Select(x => x.Id).ToListAsync()) db.Add(new RolePermission { Role = role, PermissionId = id });
            // These are the fixture's explicit store configuration, never application defaults.
            foreach (var (title, url) in new[] { ("Đơn POS", "/admin/pos/orders-page"), ("Ca POS", "/admin/pos-shift"),
                ("Lịch sử ca", "/admin/pos-shift/history"), ("Dashboard", "/admin/pos/dashboard"), ("Mẫu in", "/admin/receipt-templates"),
                ("UI24 không đưa vào menu gọn", "/admin/pos/return-to-stock"), ("UI24 dashboard khác", "/admin/pos-shift/manager-dashboard") })
                db.Add(new AdminMenuItem { StoreId = seed.StoreId, Title = title, Url = url, IsActive = true, SortOrder = url.Length });
            await db.SaveChangesAsync();
            var shift = new POSShift { StoreId = seed.StoreId, TerminalId = terminalId, WarehouseId = seed.WarehouseId,
                OpenedByUserId = user.Id, Status = POSShiftStatus.Open, ShiftCode = "UI24-OPEN" };
            db.Add(shift); await db.SaveChangesAsync();
            var order = new Order { StoreId = seed.StoreId, POSShiftId = shift.Id, Status = OrderStatus.Draft, OrderNumber = "UI24-DRAFT" };
            db.Add(order); await db.SaveChangesAsync(); shift.CurrentOrderId = order.Id; await db.SaveChangesAsync();
        }
        async Task<string> StableState()
        {
            await using var db = app.Database.CreateHostContext();
            return JsonSerializer.Serialize(new { stores = await db.Stores.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                menus = await db.AdminMenuItems.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                grants = await db.RolePermissions.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                orders = await db.Orders.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                payments = await db.OrderPayments.AsNoTracking().OrderBy(x => x.Id).ToListAsync() });
        }
        using var client = app.CreateClient();
        await app.StartWebAsync(client);
        var html = await client.GetStringAsync("/admin/account/login");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        using (var login = await client.PostAsync("/admin/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["UserName"] = userName, ["Password"] = password, ["SelectedTerminalId"] = terminalId.ToString(), ["__RequestVerificationToken"] = token }))) Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var stable = await StableState();
        await Probe("baseline");
        await app.SwitchReleaseAsync(candidate, client);
        using (var afterSwitch = await client.GetAsync("/admin/pos/v3")) Assert.Equal(HttpStatusCode.OK, afterSwitch.StatusCode);
        // Opt-in addendum: probe the new read contract from the actual published DLL under HTTPS.
        if (Environment.GetEnvironmentVariable("GAO_UI24_LEDGER_ADDENDUM") == "1")
        {
            using var period = await client.GetAsync($"/admin/inventory-ledger/period?warehouseId={seed.WarehouseId}&productVariantId={seed.ProductVariantId}");
            Assert.Equal(HttpStatusCode.OK, period.StatusCode);
            Assert.True(period.Headers.CacheControl?.NoStore);
            var json = await period.Content.ReadAsStringAsync();
            using var value = JsonDocument.Parse(json);
            Assert.False(value.RootElement.GetProperty("hasRecordedHistory").GetBoolean());
            Assert.Equal(JsonValueKind.Null, value.RootElement.GetProperty("openingQty").ValueKind);
            await File.WriteAllTextAsync(Path.Combine(output, "published-ledger-period.json"), json);
        }
        await Probe("candidate");
        Assert.Equal(stable, await StableState());
        // An extracted, independently verified release is the rollback input, not the build output.
        var rollback = Release("GAO_UI24_ROLLBACK_RELEASE"); var rollbackProof = Verify(rollback);
        Assert.Equal(JsonSerializer.Serialize(baselineProof.Files), JsonSerializer.Serialize(rollbackProof.Files));
        await app.SwitchReleaseAsync(rollback, client);
        using (var afterRollback = await client.GetAsync("/admin/pos/v3")) Assert.Equal(HttpStatusCode.OK, afterRollback.StatusCode);
        await Probe("rollback");
        Assert.Equal(stable, await StableState());
        await using (var db = app.Database.CreateTenantContext(seed.StoreId))
        {
            Assert.False(db.Database.HasPendingModelChanges()); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Equal(1, await db.ProductVariantUnitBarcodes.CountAsync(x => x.ProductUnitConversionId == conversionId && x.Barcode == "8934588063060"));
        }
        Assert.Equal(baselineProof.ManifestSha256, Verify(before).ManifestSha256);
        Assert.Equal(candidateProof.ManifestSha256, Verify(candidate).ManifestSha256);
        await File.WriteAllTextAsync(Path.Combine(output, "release-checks.json"), JsonSerializer.Serialize(new
        {
            passed = true, baseline = baselineProof, candidate = candidateProof, rollback = rollbackProof,
            environment = "Production", transport = "real isolated HTTPS", trustedTestCertificateOnly = true,
            realIisDeployment = false, realStoreDataUsed = false, schemaChangedBySwitch = false,
            menusStoresGrantsOrdersPaymentsUnchanged = true, pendingModelChanges = false, pendingMigrations = 0,
            originalCookieAcceptedAfterSwitchAndRollback = true, barcodeChangeSurvivesRollback = true,
            noMigratorOrSeedDuringSwitch = true, currentRazor = CompiledViews(Path.Combine(candidate, "web", "GaoApp.Web.dll"))
        }, new JsonSerializerOptions { WriteIndented = true }));

        async Task Probe(string phase)
        {
            var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment["NODE_PATH"] = Path.Combine(root, "Logs", "pos-offline-browser-deps", "node_modules");
            start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", "ui24-release.browser.cjs"));
            using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { phase, baseUrl = app.BrowserAddress.ToString().TrimEnd('/'), user = userName, password, terminalId, conversionId, output }));
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(7)); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            var log = await stdout + "\n" + await stderr; await File.WriteAllTextAsync(Path.Combine(output, phase + ".log"), log);
            Assert.True(process.ExitCode == 0, log);
        }
    }

    private static string Release(string key)
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable(key) ?? throw new InvalidOperationException(key + " is required for this opt-in release test."));
        var workspace = FullApplicationFixture.SourceRoot() + Path.DirectorySeparatorChar;
        Assert.StartsWith(workspace, root, StringComparison.OrdinalIgnoreCase); Assert.True(File.Exists(Path.Combine(root, "release-manifest.json")));
        return root;
    }
    private sealed record PackageFile(string Path, long Bytes, string Sha256);
    private sealed record PackageProof(string Root, string ManifestSha256, PackageFile[] Files);
    private static PackageProof Verify(string root)
    {
        var manifest = Path.Combine(root, "release-manifest.json"); using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
        var components = doc.RootElement.GetProperty("components");
        Assert.Equal(new[] { "Web" }, components.ValueKind == JsonValueKind.String
            ? [components.GetString()] : components.EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.False(Directory.Exists(Path.Combine(root, "migrator")));
        var files = doc.RootElement.GetProperty("files").EnumerateArray().Select(item =>
        {
            var relative = item.GetProperty("path").GetString()!; var file = Path.GetFullPath(Path.Combine(root, relative));
            Assert.StartsWith(root + Path.DirectorySeparatorChar, file, StringComparison.OrdinalIgnoreCase);
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))); var length = new FileInfo(file).Length;
            Assert.Equal(item.GetProperty("sha256").GetString(), hash, ignoreCase: true); Assert.Equal(item.GetProperty("bytes").GetInt64(), length);
            return new PackageFile(relative, length, hash);
        }).OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        return new(root, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifest))), files);
    }
    private static string[] CompiledViews(string assembly)
    {
        using var stream = File.OpenRead(assembly); using var pe = new PEReader(stream); var metadata = pe.GetMetadataReader(); var result = new List<string>();
        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attr = metadata.GetCustomAttribute(handle); if (attr.Constructor.Kind != HandleKind.MemberReference) continue;
            var member = metadata.GetMemberReference((MemberReferenceHandle)attr.Constructor); if (member.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent); if (metadata.GetString(type.Name) != "RazorCompiledItemAttribute") continue;
            var blob = metadata.GetBlobReader(attr.Value); Assert.Equal(1, blob.ReadUInt16()); blob.ReadSerializedString(); blob.ReadSerializedString(); result.Add(blob.ReadSerializedString()!);
        }
        Assert.NotEmpty(result); Assert.Contains("/Areas/Admin/Views/BarcodeManager/Index.cshtml", result); return result.Order().ToArray();
    }
}
