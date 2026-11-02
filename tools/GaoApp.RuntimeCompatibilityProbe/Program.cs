using System.Collections;
using System.Data.Common;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

// Run twice in separate processes: net8 creates a synthetic baseline, net10
// opens that SAME owned GUID database. No application connection setting is used.
if (args.Length is < 3 or > 4 || args[0] is not ("create" or "verify" or "upgrade-audit"))
    throw new ArgumentException("Usage: create|verify|upgrade-audit <paired GaoApp.Tests.dll> <evidence directory> [baseline migration for create]");
var create = args[0] == "create";
var upgradeAudit = args[0] == "upgrade-audit";
const string legacyMigration = "20261014140000_AddBankNotificationBridge";
const string auditMigration = "20261022090000_AddDurableAuditOutbox";
if (args.Length == 4 && (!create || args[3] != legacyMigration)) throw new ArgumentException("Only the reviewed legacy baseline can be selected.");
if (Environment.Version.Major != (create ? 8 : 10)) throw new InvalidOperationException("Use the corresponding .NET 8/.NET 10 process.");
var assemblyPath = Path.GetFullPath(args[1]);
var evidence = Path.GetFullPath(args[2]);
var repository = new DirectoryInfo(AppContext.BaseDirectory);
while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "GaoApp.sln"))) repository = repository.Parent;
if (repository is null || !evidence.StartsWith(Path.Combine(repository.FullName, ".artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Compatibility evidence must stay under this workspace's .artifacts.");
var stateFile = Path.Combine(evidence, "baseline.json");
if (create && Directory.Exists(evidence)) throw new InvalidOperationException("Create requires a new evidence directory.");
if (create) Directory.CreateDirectory(evidence);
var resolver = new AssemblyDependencyResolver(assemblyPath);
AssemblyLoadContext.Default.Resolving += (_, name) => {
    var path = resolver.ResolveAssemblyToPath(name) ?? Path.Combine(Path.GetDirectoryName(assemblyPath)!, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) => {
    var path = resolver.ResolveUnmanagedDllToPath(name);
    return path is null ? IntPtr.Zero : NativeLibrary.Load(path);
};
const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
var tests = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
var fixtureType = tests.GetType("GaoApp.Tests.Configuration.InventoryPostingLocalDb", throwOnError: true)!;
var fixture = Activator.CreateInstance(fixtureType, nonPublic: true)!;
var success = false;
try {
    Baseline? baseline = create ? null : JsonSerializer.Deserialize<Baseline>(await File.ReadAllTextAsync(stateFile));
    if (!create) {
        if (baseline is null || !Regex.IsMatch(baseline.DatabaseName, "^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$")) throw new InvalidOperationException("Unsafe baseline database name.");
        fixtureType.GetField("_databaseName", flags)!.SetValue(fixture, baseline.DatabaseName);
    }
    var connectionString = (string)fixtureType.GetProperty("ConnectionString", flags)!.GetValue(fixture)!;
    var sqlType = Assembly.Load("Microsoft.Data.SqlClient").GetType("Microsoft.Data.SqlClient.SqlConnection", true)!;
    await using var connection = (DbConnection)Activator.CreateInstance(sqlType, connectionString)!;
    // Ownership is recorded before any database creation, for recoverable cleanup.
    if (create) await File.WriteAllTextAsync(Path.Combine(evidence, "owned-database.txt"), connection.Database);
    if (!upgradeAudit) await Invoke("MigrateAsync", args.Length == 4 ? args[3] : null, CancellationToken.None);
    if (create) await Invoke("SeedInventoryCatalogAsync", false, CancellationToken.None);
    await connection.OpenAsync();
    var schemaSql = """
        SELECT s.name AS [schema],t.name AS [table],c.name,c.column_id,ty.name AS [type],c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,d.definition,cc.definition AS computed
        FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id
        JOIN sys.types ty ON ty.user_type_id=c.user_type_id LEFT JOIN sys.default_constraints d ON d.object_id=c.default_object_id
        LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id
        WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name,c.column_id;
        SELECT s.name,t.name,i.name,i.type,i.is_unique,i.is_primary_key,i.filter_definition,c.name,ic.key_ordinal,ic.is_descending_key,ic.is_included_column
        FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.indexes i ON i.object_id=t.object_id
        JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=ic.column_id
        WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name,i.name,ic.index_column_id;
        SELECT OBJECT_SCHEMA_NAME(f.parent_object_id),OBJECT_NAME(f.parent_object_id),f.name,pc.name,OBJECT_SCHEMA_NAME(f.referenced_object_id),OBJECT_NAME(f.referenced_object_id),rc.name,f.delete_referential_action,f.update_referential_action
        FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
        JOIN sys.columns pc ON pc.object_id=f.parent_object_id AND pc.column_id=fc.parent_column_id
        JOIN sys.columns rc ON rc.object_id=f.referenced_object_id AND rc.column_id=fc.referenced_column_id
        ORDER BY OBJECT_SCHEMA_NAME(f.parent_object_id),OBJECT_NAME(f.parent_object_id),f.name,fc.constraint_column_id;
        SELECT OBJECT_SCHEMA_NAME(parent_object_id),OBJECT_NAME(parent_object_id),name,definition,is_disabled,is_not_trusted FROM sys.check_constraints ORDER BY name;
        SELECT s.name,o.name,o.type,m.definition FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id JOIN sys.sql_modules m ON m.object_id=o.object_id WHERE o.is_ms_shipped=0 ORDER BY s.name,o.name;
        """;
    var schema = await Fingerprint(connection, schemaSql);
    var history = await Fingerprint(connection, "SELECT MigrationId,ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId");
    var data = await Fingerprint(connection, "SELECT * FROM dbo.Stores ORDER BY Id;SELECT * FROM dbo.Products ORDER BY Id;SELECT * FROM dbo.ProductVariant ORDER BY Id;SELECT * FROM dbo.Unit ORDER BY Id;SELECT * FROM dbo.Warehouses ORDER BY Id;");
    if (upgradeAudit) {
        if (baseline!.Schema != schema || baseline.History != history || baseline.Data != data) throw new InvalidOperationException("The legacy baseline changed before the upgrade test.");
        await using var context = (IAsyncDisposable)fixtureType.GetMethod("CreateHostContext", flags)!.Invoke(fixture, new object?[] { null })!;
        var infrastructure = context.GetType().GetInterfaces().Single(x => x.IsGenericType && x.GetGenericTypeDefinition().FullName == "Microsoft.EntityFrameworkCore.Infrastructure.IInfrastructure`1" && x.GetGenericArguments()[0] == typeof(IServiceProvider));
        var services = (IServiceProvider)infrastructure.GetProperty("Instance")!.GetValue(context)!;
        var migratorType = Assembly.Load("Microsoft.EntityFrameworkCore.Relational").GetType("Microsoft.EntityFrameworkCore.Migrations.IMigrator", true)!;
        var migrator = services.GetService(migratorType)!;
        var generator = migratorType.GetMethod("GenerateScript")!;
        var sql = (string)generator.Invoke(migrator, new[] { legacyMigration, auditMigration, Enum.ToObject(generator.GetParameters()[2].ParameterType, 0) })!;
        if (sql.Contains("AdminMenuItems", StringComparison.OrdinalIgnoreCase) || sql.Contains("DROP ", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected destructive/menu changes in the reviewed audit migration.");
        await File.WriteAllTextAsync(Path.Combine(evidence, "review-79-to-80.sql"), sql);
        await Invoke("MigrateAsync", null, CancellationToken.None);
        // Remove only the reviewed additive audit schema from the comparison.
        var legacySchemaSql = schemaSql.Replace("WHERE t.is_ms_shipped=0", "WHERE t.is_ms_shipped=0 AND t.name<>N'AuditOutbox' AND NOT (t.name=N'AuditLogs' AND c.name=N'EventId')")
            .Replace("AND NOT (t.name=N'AuditLogs' AND c.name=N'EventId') ORDER BY s.name,t.name,i.name", "AND i.name<>N'IX_AuditLogs_EventId' ORDER BY s.name,t.name,i.name")
            .Replace("FROM sys.check_constraints ORDER BY name", "FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id)<>N'AuditOutbox' ORDER BY name");
        if (await Fingerprint(connection, legacySchemaSql) != baseline.Schema) throw new InvalidOperationException("Schema changes exceed the reviewed audit additions.");
        var legacyHistory = await Fingerprint(connection, $"SELECT MigrationId,ProductVersion FROM dbo.__EFMigrationsHistory WHERE MigrationId<=N'{legacyMigration}' ORDER BY MigrationId");
        if (legacyHistory != baseline.History) throw new InvalidOperationException("Existing migration history was modified.");
        await using var count = connection.CreateCommand(); count.CommandText="SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;";
        if (Convert.ToInt32(await count.ExecuteScalarAsync()) != 80) throw new InvalidOperationException("Expected exactly 80 migrations after the additive upgrade.");
    }
    var keys = new DirectoryInfo(Path.Combine(evidence, "keys"));
    var pfxPath = Path.Combine(evidence, "test-key.pfx");
    const string password = "Synthetic-compatibility-password-23!";
    const string pfxPassword = "Synthetic-ephemeral-PFX-23!";
    if (create) {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=GaoApp runtime compatibility test only", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var temporary = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(2));
        await File.WriteAllBytesAsync(pfxPath, temporary.Export(X509ContentType.Pfx, pfxPassword));
    }
#if NET10_0_OR_GREATER
    using var certificate = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, pfxPassword, X509KeyStorageFlags.EphemeralKeySet);
#else
    using var certificate = new X509Certificate2(pfxPath, pfxPassword, X509KeyStorageFlags.EphemeralKeySet);
#endif
    var provider = DataProtectionProvider.Create(keys, builder => builder.SetApplicationName("GaoApp").ProtectKeysWithCertificate(certificate));
    var protector = provider.CreateProtector("GaoApp.RuntimeCompatibilityProbe");
    var cookieProtector = provider.CreateProtector("Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", "Cookies", "v2");
    var format = new TicketDataFormat(cookieProtector);
    var hasher = new PasswordHasher<object>();
    if (create) {
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "synthetic-23"), new Claim(ClaimTypes.Name, "Upgrade synthetic account") }, "Cookies")), new AuthenticationProperties { ExpiresUtc=DateTimeOffset.UtcNow.AddHours(4) }, "Cookies");
        baseline = new(connection.Database, schema, history, data, hasher.HashPassword(new object(), password), protector.Protect("synthetic-credential-before-upgrade"), format.Protect(ticket));
        await File.WriteAllTextAsync(stateFile, JsonSerializer.Serialize(baseline, new JsonSerializerOptions { WriteIndented=true }));
    } else {
        if (baseline!.Schema != schema || baseline.History != history || baseline.Data != data) throw new InvalidOperationException("Upgrade changed SQL schema, migration history or synthetic catalog data.");
        if (upgradeAudit && await Fingerprint(connection, "SELECT * FROM dbo.Stores ORDER BY Id;SELECT * FROM dbo.Products ORDER BY Id;SELECT * FROM dbo.ProductVariant ORDER BY Id;SELECT * FROM dbo.Unit ORDER BY Id;SELECT * FROM dbo.Warehouses ORDER BY Id;") != baseline.Data) throw new InvalidOperationException("Audit schema upgrade changed catalog data.");
        if (hasher.VerifyHashedPassword(new object(), baseline.PasswordHash, password) == PasswordVerificationResult.Failed ||
            hasher.VerifyHashedPassword(new object(), baseline.PasswordHash, password + "wrong") != PasswordVerificationResult.Failed) throw new InvalidOperationException("Legacy password hash compatibility failed.");
        if (protector.Unprotect(baseline.ProtectedCredential) != "synthetic-credential-before-upgrade") throw new InvalidOperationException("Legacy encrypted credential compatibility failed.");
        if (format.Unprotect(baseline.Cookie)?.Principal.FindFirstValue(ClaimTypes.NameIdentifier) != "synthetic-23") throw new InvalidOperationException("Legacy authentication ticket compatibility failed.");
        await using var context = (IAsyncDisposable)fixtureType.GetMethod("CreateHostContext", flags)!.Invoke(fixture, new object?[] { null })!;
        var variants = (IEnumerable)context.GetType().GetProperty("ProductVariants")!.GetValue(context)!;
        if (variants.Cast<object>().Count() != 1) throw new InvalidOperationException("EF 10 could not query the EF 8 catalog.");
        await File.WriteAllTextAsync(Path.Combine(evidence, "verified.json"), JsonSerializer.Serialize(new { runtime=Environment.Version.ToString(), database=connection.Database, schemaUnchanged=!upgradeAudit, onlyReviewedAuditSchemaAdded=upgradeAudit, migrationHistoryUnchanged=!upgradeAudit, originalHistoryPreserved=true, catalogDataUnchanged=true, legacyPasswordHash=true, wrongPasswordRejected=true, legacyEncryptedCredential=true, legacyCookie=true, ef10ReadsEf8Catalog=true }, new JsonSerializerOptions { WriteIndented=true }));
    }
    success = true;
    Console.WriteLine(create ? "PASS: genuine .NET 8 / EF 8 baseline created in owned test database." : upgradeAudit
        ? "PASS: legacy .NET 8 / migration 79 to .NET 10 / migration 80 — only reviewed audit additions; original schema/history/catalog, passwords, keys and ticket preserved."
        : "PASS: .NET 8 to .NET 10 — unchanged schema/history/data; passwords, encrypted keys, auth ticket and EF catalog read compatible.");
} finally {
    if (!create || !success) await ((IAsyncDisposable)fixture).DisposeAsync();
}
async Task Invoke(string method, params object?[] parameters) {
    await (Task)fixtureType.GetMethod(method, flags)!.Invoke(fixture, parameters)!;
}
static async Task<string> Fingerprint(DbConnection connection, string sql) {
    await using var command = connection.CreateCommand(); command.CommandText=sql; command.CommandTimeout=60;
    await using var reader = await command.ExecuteReaderAsync();
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    do {
        while (await reader.ReadAsync()) {
            var row = new object[reader.FieldCount]; reader.GetValues(row);
            hash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(row)));
        }
        hash.AppendData(new byte[] { 0 });
    } while (await reader.NextResultAsync());
    return Convert.ToHexString(hash.GetHashAndReset());
}
record Baseline(string DatabaseName, string Schema, string History, string Data, string PasswordHash, string ProtectedCredential, string Cookie);
