using System.Text;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

Console.OutputEncoding = Encoding.UTF8;
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: dotnet GaoApp.SchemaInspect.dll <Migrator-directory> <new-report.json>");
    return 2;
}

var stage = "configuration";
try
{
    var directory = Path.GetFullPath(args[0]);
    var output = Path.GetFullPath(args[1]);
    if (File.Exists(output))
    {
        Console.Error.WriteLine("Report already exists. Choose a new filename; no database access attempted.");
        return 2;
    }
    var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
    var config = new ConfigurationBuilder().SetBasePath(directory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{environment}.json", optional: true)
        .AddEnvironmentVariables().Build();
    var connection = config.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connection))
    {
        Console.Error.WriteLine("ConnectionStrings:DefaultConnection is empty. Check the Migrator configuration.");
        return 2;
    }
    Console.WriteLine($"READ ONLY | Environment: {environment} | No migration, seed, or repair will run.");
    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options;
    await using var db = new AppDbContext(options, new InspectTenant(), new InspectUser());
    stage = "schema inspection";
    var report = await new DatabaseSchemaInspection(db).ReadAsync();
    stage = "report writing";
    await using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await JsonSerializer.SerializeAsync(file, report, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine($"Status: {report.Status}; Source: {report.SourceMigrations.Count}; Applied: {report.AppliedMigrations.Count}; Pending: {report.PendingMigrations.Count}");
    Console.WriteLine($"Expected-only records: {report.ExpectedOnly.Count}; Actual-only records: {report.ActualOnly.Count}");
    Console.WriteLine($"Report: {output}");
    return 0; // Successful collection does not mean that migration is safe.
}
catch (Exception ex)
{
    // Do not emit provider messages, connection strings, or credentials.
    Console.Error.WriteLine($"Inspection failed during {stage}: {ex.GetType().Name}. No migration or repair was attempted.");
    return 1;
}

sealed class InspectTenant : ITenantContext
{
    public int? StoreId => null;
    public bool IsHostAdmin => true;
    public string? Subdomain => null;
}
sealed class InspectUser : ICurrentUser
{
    public int? UserId => null;
    public string? UserName => null;
    public int? TerminalId => null;
    public string? TerminalCode => null;
    public bool IsAuthenticated => false;
}
