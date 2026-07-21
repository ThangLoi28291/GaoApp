using GaoApp.Application;
using GaoApp.Application.Common.Options;
using GaoApp.Infrastructure;
using GaoApp.Migrator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting GaoApp.Migrator");

    var builder = Host.CreateApplicationBuilder(args);

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
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddSingleton<IWebHostEnvironment>(sp =>
        new MigratorWebHostEnvironment(
            sp.GetRequiredService<IHostEnvironment>()));
    builder.Services.Configure<SeedDataOptions>(
        builder.Configuration.GetSection(SeedDataOptions.SectionName));

    builder.Services.AddScoped<MigrationRunner>();

    var host = builder.Build();

    using var scope = host.Services.CreateScope();

    Log.Information(
        "Running migration and seed for environment: {EnvironmentName}",
        builder.Environment.EnvironmentName);

    var runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
    await runner.RunAsync();

    Log.Information("GaoApp.Migrator completed successfully");
}
catch (Exception ex)
{
    Log.Fatal(ex, "GaoApp.Migrator terminated unexpectedly");
    // The exception has already been recorded with its full stack trace.
    // Do not rethrow from the console entry point: on Windows that turns a
    // configuration error into the opaque 0xe0434352 application-error dialog.
    // A non-zero exit code still lets scripts/deployments detect the failure.
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
