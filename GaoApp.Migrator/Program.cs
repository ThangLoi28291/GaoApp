using GaoApp.Application;
using GaoApp.Infrastructure;
using GaoApp.Migrator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

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
    throw;
}
finally
{
    Log.CloseAndFlush();
}