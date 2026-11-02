using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Storefront.Web.Services;

// Used only by the normal framework repository pipeline. No scanner, key export,
// custom encryption, adoption, repair, or private XML diagnostic API is provided.
public sealed class StorefrontProtectedKeyRepository(IXmlRepository inner) : IXmlRepository
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, string> seen = [];
    private HashSet<Guid> current = [];
    private bool failed;
    private bool writesAllowed;
    public IReadOnlySet<Guid> KeyIds { get { lock (sync) return current.ToHashSet(); } }
    public void PermitValidatedWrites() { lock (sync) { if (failed) throw Invalid(); writesAllowed = true; } }
    public void ForbidWrites() { lock (sync) { failed = true; writesAllowed = false; } }
    private static CryptographicException Invalid() => new("Storefront protected key ring is unavailable.");

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (sync)
        {
            if (failed) throw Invalid();
            try
            {
                var elements = inner.GetAllElements(); var ids = new HashSet<Guid>();
                foreach (var element in elements)
                {
                    if (element.Name == "revocation") continue; // Framework validates revocation semantics.
                    var id = ValidateKey(element);
                    if (!ids.Add(id)) throw Invalid();
                    var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(element.ToString(SaveOptions.DisableFormatting))));
                    if (seen.TryGetValue(id, out var prior) && prior != digest) throw Invalid();
                    seen[id] = digest;
                }
                if (!current.IsSubsetOf(ids)) throw Invalid();
                current = ids;
                return elements;
            }
            catch { failed = true; writesAllowed = false; throw Invalid(); }
        }
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        lock (sync)
        {
            if (failed || !writesAllowed) throw Invalid();
            try
            {
                if (element.Name != "revocation") _ = ValidateKey(element);
                inner.StoreElement(element, friendlyName);
            }
            catch { failed = true; writesAllowed = false; throw Invalid(); }
        }
    }

    private static Guid ValidateKey(XElement element)
    {
        if (element.Name != "key" || !Guid.TryParse((string?)element.Attribute("id"), out var id) || id == Guid.Empty || (string?)element.Attribute("version") != "1") throw Invalid();
        var secrets = element.Descendants(XName.Get("encryptedSecret", "http://schemas.asp.net/2015/03/dataProtection")).ToArray();
        if (secrets.Length != 1 || element.Descendants("masterKey").Any() ||
            ((string?)secrets[0].Attribute("decryptorType"))?.Split(',')[0].Trim() != "Microsoft.AspNetCore.DataProtection.XmlEncryption.DpapiXmlDecryptor" ||
            secrets[0].Elements().Count() != 1 || secrets[0].Element("encryptedKey")?.Element("value") is not { } value || string.IsNullOrWhiteSpace(value.Value)) throw Invalid();
        // The marker establishes the framework DPAPI envelope, not its historical
        // CurrentUser scope. Actual decryption is forced through every IKey below.
        return id;
    }
}

public sealed class StorefrontProtectedKeys(IConfiguration configuration, IWebHostEnvironment environment,
    StorefrontCheckoutReadiness path, IServiceProvider services)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? failureCode;
    private bool continuityChecked;
    private int? provedStore;
    public bool Required => StorefrontHostPolicy.From(configuration, environment).Mode == StorefrontRuntimeMode.Release;
    public bool ProtectionRequested => configuration["StorefrontHost:KeyProtection"] == "WindowsDpapiCurrentUser";

    // Fixed reason codes only. Existing bad rings stay untouched and the process
    // remains live/closed so an operator can restore the correct retained ring.
    public async Task<string?> EnsureAsync(AppDbContext db, CancellationToken ct)
    {
        if (!Required && !ProtectionRequested) return null;
        if (!path.Ready) return "KEY_DIRECTORY_INVALID";
        if (!ProtectionRequested || !OperatingSystem.IsWindows()) return "KEY_PROTECTION_UNSUPPORTED";
        await gate.WaitAsync(ct);
        StorefrontProtectedKeyRepository? repository = null;
        try
        {
            if (failureCode is not null) return failureCode;
            repository = services.GetRequiredService<StorefrontProtectedKeyRepository>();
            var manager = services.GetRequiredService<IKeyManager>();
            var keys = manager.GetAllKeys(); // Normal repository read; no automatic key generation.
            if (!repository.KeyIds.SetEquals(keys.Select(x => x.KeyId))) throw new CryptographicException();
            foreach (var key in keys) if (key.CreateEncryptor() is null) throw new CryptographicException();
            if (db.CurrentStoreId is not > 0 || db.CanReadAllStores) return "STORE_SCOPE_INVALID";
            var store = db.CurrentStoreId.Value;
            if (provedStore.HasValue && provedStore != store) throw new CryptographicException();
            if (keys.Count == 0)
            {
                // Includes archived/soft-deleted orders; own scope is explicit.
                var prior = await db.StorefrontOrders.IgnoreQueryFilters().AnyAsync(x => x.StoreId == store, ct) ||
                    await db.Set<StorefrontCommandReceipt>().IgnoreQueryFilters().AnyAsync(x => x.StoreId == store && x.Operation == "place-cod-v1", ct) ||
                    await db.Set<StorefrontPaymentIntent>().IgnoreQueryFilters().AnyAsync(x => x.StoreId == store, ct);
                if (prior) { repository.ForbidWrites(); return failureCode = "KEYRING_MISSING_PROOF"; }
                if (!bool.TryParse(configuration["StorefrontHost:ReleaseEnabled"], out var bootstrap) || !bootstrap)
                    return "KEYRING_BOOTSTRAP_HELD";
            }
            else if (!continuityChecked)
            {
                // Verify actual public placement outcomes with their genuine
                // purpose. A different valid ring must not replace historical
                // public proof merely because its own current key decrypts.
                var protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("GaoApp.Storefront.Placement.v1", store.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var last = 0; var count = 0;
                while (true)
                {
                    var batch = await db.Set<StorefrontCommandReceipt>().IgnoreQueryFilters().AsNoTracking()
                        .Where(x => x.StoreId == store && x.Operation == "place-cod-v1" && x.Id > last).OrderBy(x => x.Id).Take(100).Select(x => new { x.Id, x.OutcomeJson }).ToListAsync(ct);
                    if (batch.Count == 0) break;
                    foreach (var row in batch)
                    {
                        using var json = JsonDocument.Parse(row.OutcomeJson);
                        var envelope = json.RootElement.GetProperty("protected").GetString() ?? throw new CryptographicException();
                        _ = protector.Unprotect(envelope); count++; last = row.Id;
                    }
                }
                if (count == 0 && await db.StorefrontOrders.IgnoreQueryFilters().AnyAsync(x => x.StoreId == store, ct))
                { repository.ForbidWrites(); return failureCode = "KEYRING_PROOF_UNVERIFIED"; }
            }
            // A pre-W07 empty database already has orders/intents/receipts tables.
            // Their emptiness cannot attest current proof guards. Reuse the real
            // read-only schema/history comparer before admitting any key write;
            // never migrate, baseline, seed or repair here.
            try
            {
                var schema = await new DatabaseSchemaInspection(db).ReadAsync(ct);
                if (schema.Status != "MatchingCurrentSchema" || schema.PendingMigrations.Count != 0)
                    return "CURRENT_SCHEMA_REQUIRED";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return "CURRENT_SCHEMA_REQUIRED"; }
            repository.PermitValidatedWrites();
            var now = DateTimeOffset.UtcNow;
            // Automatic generation is disabled: only this validation boundary may
            // ask the standard manager to bootstrap/rotate an actual DPAPI key.
            if (!keys.Any(x => !x.IsRevoked && x.ActivationDate <= now && x.ExpirationDate > now.AddDays(2)))
            {
                _ = manager.CreateNewKey(now, now.AddDays(90));
                keys = manager.GetAllKeys();
                if (!repository.KeyIds.SetEquals(keys.Select(x => x.KeyId))) throw new CryptographicException();
                foreach (var key in keys) if (key.CreateEncryptor() is null) throw new CryptographicException();
            }
            continuityChecked = true;
            provedStore = store;
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            repository?.ForbidWrites();
            return failureCode ??= "KEYRING_UNAVAILABLE";
        }
        finally { gate.Release(); }
    }
}

