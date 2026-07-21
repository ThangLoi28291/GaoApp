using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.SqlServer.Design.Internal;
using Microsoft.Extensions.DependencyInjection;

const string migrationId = "20260718165310_Phase229CanaryActivation";
const string migrationName = "Phase229CanaryActivation";
const string migrationNamespace = "GaoApp.Infrastructure.Migrations";
const string snapshotName = "AppDbContextModelSnapshot";

var assemblyPath = Path.GetFullPath("GaoApp.Infrastructure.pre-recovery.dll");
var assembly = Assembly.LoadFrom(assemblyPath);
var migrationType = assembly.GetType($"{migrationNamespace}.{migrationName}", throwOnError: true)!;
var contextType = assembly.GetType("GaoApp.Infrastructure.Data.AppDbContext", throwOnError: true)!;
var migration = (Migration)Activator.CreateInstance(migrationType)!;

var upBuilder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
var downBuilder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
InvokeMigrationMethod(migrationType, migration, "Up", upBuilder);
InvokeMigrationMethod(migrationType, migration, "Down", downBuilder);

var modelBuilder = new ModelBuilder(new ConventionSet());
migrationType
    .GetMethod("BuildTargetModel", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(migration, new object[] { modelBuilder });

var services = new ServiceCollection();
services.AddEntityFrameworkSqlServer();
services.AddEntityFrameworkDesignTimeServices();
new SqlServerDesignTimeServices().ConfigureDesignTimeServices(services);
using var serviceProvider = services.BuildServiceProvider();
var targetModel = serviceProvider
    .GetRequiredService<IModelRuntimeInitializer>()
    .Initialize(modelBuilder.FinalizeModel(), designTime: true);
var generator = serviceProvider.GetRequiredService<IMigrationsCodeGeneratorSelector>().Select("C#");

var outputDirectory = Path.GetFullPath("generated");
Directory.CreateDirectory(outputDirectory);
File.WriteAllText(
    Path.Combine(outputDirectory, $"{migrationId}.cs"),
    generator.GenerateMigration(migrationNamespace, migrationName, upBuilder.Operations, downBuilder.Operations));
File.WriteAllText(
    Path.Combine(outputDirectory, $"{migrationId}.Designer.cs"),
    generator.GenerateMetadata(migrationNamespace, contextType, migrationName, migrationId, targetModel));
File.WriteAllText(
    Path.Combine(outputDirectory, $"{snapshotName}.cs"),
    generator.GenerateSnapshot(migrationNamespace, contextType, snapshotName, targetModel));

Console.WriteLine($"Recovered {migrationId}: {upBuilder.Operations.Count} up operations, {downBuilder.Operations.Count} down operations.");
Console.WriteLine(outputDirectory);

static void InvokeMigrationMethod(Type type, Migration migration, string name, MigrationBuilder builder)
{
    type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(migration, new object[] { builder });
}
