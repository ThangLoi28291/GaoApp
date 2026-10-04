using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

if (args.Length != 2 || args[0] is not ("--check" or "--apply"))
{
    Console.Error.WriteLine("Usage: dotnet GaoApp.SchemaRepair.dll --check|--apply <Migrator-directory>");
    return 2;
}
try
{
    var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
    var config = new ConfigurationBuilder().SetBasePath(Path.GetFullPath(args[1]))
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{environment}.json", optional: true).AddEnvironmentVariables().Build();
    var connection = config.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connection)) throw new MigratorConfigurationException("ConnectionStrings:DefaultConnection is empty.");
    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection, sql => sql.CommandTimeout(180)).Options;
    await using var db = new AppDbContext(options, new RepairTenant(), new RepairUser());
    Console.WriteLine(args[0] == "--check" ? "CHECK ONLY: no schema or data writes." : "APPLY: create only the verified missing AutoInvoice indexes in one transaction. No data or history changes.");
    var result = await new AutoInvoiceIndexRepair(db).RunAsync(args[0] == "--apply");
    foreach (var name in result.Indexes) Console.WriteLine(name);
    Console.WriteLine(result.Status);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Repair failed: {ex.GetType().Name}");
    if (ex is MigratorConfigurationException or DatabaseCompatibilityException) Console.Error.WriteLine(ex.Message);
    return 1;
}

sealed class RepairTenant : ITenantContext
{
    public int? StoreId => null;
    public bool IsHostAdmin => true;
    public string? Subdomain => null;
}
sealed class RepairUser : ICurrentUser
{
    public int? UserId => null;
    public string? UserName => null;
    public int? TerminalId => null;
    public string? TerminalCode => null;
    public bool IsAuthenticated => false;
}
