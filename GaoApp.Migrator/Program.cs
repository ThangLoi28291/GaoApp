using GaoApp.Application;
using GaoApp.Application.Common.Options;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Interfaces.Services.Purchases;
using GaoApp.Application.Services.Inventory;
using GaoApp.Infrastructure;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Data.Seed;
using GaoApp.Migrator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Serilog;
using Serilog.Events;

MigratorMode mode;
try { mode = MigratorCommand.Parse(args); }
catch (MigratorConfigurationException)
{
    Console.Error.WriteLine(MigratorCommand.Usage);
    Environment.ExitCode = 2;
    return;
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting GaoApp.Migrator");

    var builder = Host.CreateApplicationBuilder(Array.Empty<string>());

    // QUAN TRỌNG:
    // ép base path về thư mục output của chính executable
    // để console app luôn đọc đúng appsettings.json của Migrator
    builder.Configuration.Sources.Clear();
    builder.Configuration
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .AddJsonFile(
            $"appsettings.{builder.Environment.EnvironmentName}.json",
            optional: true,
            reloadOnChange: false)
        .AddEnvironmentVariables();



    builder.Services.AddSerilog((services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();
    });

    // Migrator only needs the persistence/infrastructure graph used by
    // AppDbContext and the security seeders. Registering the whole Application
    // graph here also pulls in request/POS services that only make sense in the
    // Web host (for example ICurrentPOSContext).
    builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
    // The migrator only needs persistence, schema inspection and seed services.
    // Remove Web/POS runtime services whose constructors require request-scoped
    // application contexts that intentionally do not exist in this host.
    builder.Services.RemoveAll<IReceiptBarcodeProposalService>();
    builder.Services.RemoveAll<IReceiptIntakeCatalog>();
    builder.Services.RemoveAll<ICustomerDepositService>();
    builder.Services.RemoveAll<ICustomerReceivableService>();
    builder.Services.RemoveAll<GaoApp.Application.Interfaces.Services.Customers.ICustomerProfileReader>();
    builder.Services.RemoveAll<GaoApp.Application.Interfaces.Services.Delivery.IDeliveryFoundationService>();
    builder.Services.RemoveAll<GaoApp.Application.Interfaces.Services.Reports.IOperationsReportService>();
    builder.Services.RemoveAll<IReceiptInvoiceBackfillService>();
    builder.Services.AddSingleton<
        IInputInvoiceXmlDocumentParser,
        InputInvoiceXmlDocumentParser>();
    builder.Services.AddSingleton<IWebHostEnvironment>(sp =>
        new MigratorWebHostEnvironment(
            sp.GetRequiredService<IHostEnvironment>()));
    builder.Services
        .AddOptions<SeedDataOptions>()
        .Bind(builder.Configuration.GetSection(
            SeedDataOptions.SectionName))
        .Validate(
            options => options.HasValidConfiguration(),
            "SeedData configuration is invalid. SeedData:DemoUserPassword is required when SeedData:EnableDemoSeed is true.");
    builder.Services
        .AddOptions<ProductionBootstrapOptions>()
        .Bind(builder.Configuration.GetSection(
            ProductionBootstrapOptions.SectionName))
        .Validate(
            options => options.HasValidConfiguration(),
            "ProductionBootstrap configuration is invalid. Check required keys, field lengths, code formats, and password strength.");

    builder.Services.AddSingleton<MigratorConfigurationValidator>();
    builder.Services.AddScoped<IDatabaseMigrationCatalog,
        EfCoreDatabaseMigrationCatalog>();
    builder.Services.AddScoped<IDatabaseSchemaManifestCatalog,
        EfCoreDatabaseSchemaManifestCatalog>();
    builder.Services.AddScoped<ISqlServerDatabaseObjectInventoryReader,
        SqlServerDatabaseObjectInventoryReader>();
    builder.Services.AddScoped<ISqlServerSchemaSnapshotReader,
        SqlServerSchemaSnapshotReader>();
    builder.Services.AddScoped<IDatabaseBaselinePreflight,
        SqlServerDatabaseBaselinePreflight>();
    builder.Services.AddScoped<IDatabaseMigrationExecutor,
        EfCoreDatabaseMigrationExecutor>();
    builder.Services.AddScoped<IMandatorySecuritySeeder,
        MandatorySecuritySeeder>();
    builder.Services.AddScoped<IDemoDataSeeder, DemoDataSeeder>();
    builder.Services.AddScoped<IProvisioningTransactionRunner,
        EfCoreProvisioningTransactionRunner>();
    builder.Services.AddScoped<ProductionBootstrapper>();
    builder.Services.AddScoped<IProductionBootstrapper>(services =>
        services.GetRequiredService<ProductionBootstrapper>());
    builder.Services.AddScoped<MigrationExecutionPipeline>();
    builder.Services.AddScoped<MigrationRunner>();

    var host = builder.Build();

    using var scope = host.Services.CreateScope();

    Log.Information(
        "Running explicit Migrator operation for environment: {EnvironmentName}",
        builder.Environment.EnvironmentName);

    var runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
    await runner.RunAsync(mode);

    Log.Information("GaoApp.Migrator completed successfully");
}
catch (Exception ex)
{
    // Provider exceptions can include sensitive connection details.
    Log.Fatal("GaoApp.Migrator failed: {ErrorType}", ex.GetType().Name);
    if (ex is MigratorConfigurationException or DatabaseCompatibilityException or ProductionBootstrapStateException)
        Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
