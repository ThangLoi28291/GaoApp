using GaoApp.Application.Common.Interfaces;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.SqlServer.Design.Internal;
using Microsoft.Extensions.DependencyInjection;

// Metadata/scaffolding only. This tool never opens a connection or runs SQL.
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=Storefront_Metadata_Only;Integrated Security=true;Encrypt=false").Options;
var tenant = new TenantContext();
tenant.SetHostAdmin(); tenant.SetAuthenticatedHostAdmin(true);
using var db = new AppDbContext(options, tenant, new DesignUser());
var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
var previous = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot, designTime: true);
var current = db.GetService<IDesignTimeModel>().Model;
var differences = db.GetService<IMigrationsModelDiffer>().GetDifferences(previous.GetRelationalModel(), current.GetRelationalModel());
Console.WriteLine($"Model operations: {differences.Count}");
foreach (var group in differences.GroupBy(x => x.GetType().Name)) Console.WriteLine($"{group.Key}: {group.Count()}");
if (args is ["scaffold"])
{
    if (differences.Count == 0) throw new InvalidOperationException("No model changes to scaffold.");
    if (differences.Any(x => x switch
        {
            CreateTableOperation table => !table.Name.StartsWith("Storefront", StringComparison.Ordinal),
            CreateIndexOperation index => !index.Table.StartsWith("Storefront", StringComparison.Ordinal),
            AddUniqueConstraintOperation unique => !unique.Columns.Contains("Id") || !unique.Columns.Contains("StoreId"),
            _ => true
        })) throw new InvalidOperationException("Refusing unrelated or destructive model changes.");
    var services = new ServiceCollection();
    services.AddEntityFrameworkDesignTimeServices();
    services.AddDbContextDesignTimeServices(db);
    new SqlServerDesignTimeServices().ConfigureDesignTimeServices(services);
    using var provider = services.BuildServiceProvider();
    // Generate from the validated diff explicitly; no implicit snapshot discovery.
    var generator = provider.GetRequiredService<IMigrationsCodeGeneratorSelector>().Select("C#");
    var down = db.GetService<IMigrationsModelDiffer>().GetDifferences(current.GetRelationalModel(), previous.GetRelationalModel());
    const string ns = "GaoApp.Infrastructure.Migrations";
    const string name = "AddStorefrontFoundation";
    var directory = Path.GetFullPath("GaoApp.Infrastructure/Migrations");
    var migrationPath = Path.Combine(directory, StorefrontFoundationSchemaSqlCatalog.MigrationId + ".cs");
    var designerPath = Path.Combine(directory, StorefrontFoundationSchemaSqlCatalog.MigrationId + ".Designer.cs");
    if (File.Exists(migrationPath) || File.Exists(designerPath)) throw new InvalidOperationException("Refusing to overwrite a migration.");
    File.WriteAllText(migrationPath, generator.GenerateMigration(ns, name, differences, down));
    File.WriteAllText(designerPath, generator.GenerateMetadata(ns, typeof(AppDbContext), name, StorefrontFoundationSchemaSqlCatalog.MigrationId, current));
    File.WriteAllText(Path.Combine(directory, "AppDbContextModelSnapshot.cs"), generator.GenerateSnapshot(ns, typeof(AppDbContext), "AppDbContextModelSnapshot", current));
    Console.WriteLine(migrationPath);
    Console.WriteLine(designerPath);
}
else if (args is ["manifest"])
{
    var catalog = new EfCoreDatabaseSchemaManifestCatalog(db);
    var ids = db.Database.GetMigrations().ToList();
    for (var count = 1; count <= ids.Count; count++)
    {
        try { catalog.TryGetManifestForAppliedMigrationPrefix(ids.Take(count).ToArray(), out _); }
        catch { Console.WriteLine("First manifest failure: " + ids[count - 1]); throw; }
    }
    Console.WriteLine("Schema manifest: " + catalog.GetCurrentManifest().Tables.Count + " tables");
}
else if (args.Length != 0) throw new ArgumentException("Use no arguments for metadata diff, or scaffold.");

sealed class DesignUser : ICurrentUser
{
    public int? UserId => null;
    public string? UserName => null;
    public int? TerminalId => null;
    public string? TerminalCode => null;
    public bool IsAuthenticated => false;
}
