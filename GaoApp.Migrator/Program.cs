using GaoApp.Application;
using GaoApp.Infrastructure;
using GaoApp.Migrator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

// =========================================================
// Bootstrap logger:
// Dùng để bắt log rất sớm khi app khởi động,
// kể cả khi lỗi xảy ra trước lúc host build xong.
// =========================================================
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting GaoApp.Migrator");

    var builder = Host.CreateApplicationBuilder(args);

    // =========================================================
    // Gắn Serilog vào Host:
    // - toàn bộ ILogger<T> sẽ đi qua Serilog
    // - cấu hình thật đọc từ appsettings.json
    // =========================================================
    builder.Services.AddSerilog((services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();
    });

    // =========================================================
    // 1. Đọc cấu hình từ appsettings copy từ GaoApp.Web
    // =========================================================
    builder.Configuration
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .AddJsonFile(
            $"appsettings.{builder.Environment.EnvironmentName}.json",
            optional: true,
            reloadOnChange: false)
        .AddEnvironmentVariables();

    // =========================================================
    // 2. Đăng ký dependency nền
    // =========================================================
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // =========================================================
    // 3. Runner migrate + seed
    // =========================================================
    builder.Services.AddScoped<MigrationRunner>();

    var host = builder.Build();

    using var scope = host.Services.CreateScope();

    Log.Information("Running migration and seed");

    var runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
    await runner.RunAsync();

    Log.Information("GaoApp.Migrator completed successfully");
}
catch (Exception ex)
{
    Log.Fatal(ex, "GaoApp.Migrator terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}