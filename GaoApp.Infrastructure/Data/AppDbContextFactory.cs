using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace GaoApp.Infrastructure.Data;

/// <summary>
/// Factory dùng cho EF Core design-time:
/// - Add-Migration
/// - Update-Database
///
/// Vì AppDbContext của dự án đang có constructor:
/// AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant, ICurrentUser currentUser)
///
/// nên ở design-time phải tự tạo:
/// - TenantContext giả
/// - CurrentUser giả
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string UserSecretsId = "GaoApp-Web-Phase2-Development";

    public AppDbContext CreateDbContext(string[] args)
    {
        var basePath = ResolveWebContentRoot();
        var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Development";

        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true);

        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            configurationBuilder.AddUserSecrets(UserSecretsId);
        }

        var configuration = configurationBuilder
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Không tìm thấy connection string 'DefaultConnection'.");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        // Tenant giả cho design-time
        // Migration chỉ cần model metadata, không cần tenant runtime thật.
        var tenant = new TenantContext();
        tenant.SetHostAdmin();

        // User giả cho design-time
        ICurrentUser currentUser = new DesignTimeCurrentUser();

        return new AppDbContext(optionsBuilder.Options, tenant, currentUser);
    }

    private static string ResolveWebContentRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            var currentAppSettings = Path.Combine(current.FullName, "appsettings.json");
            if (string.Equals(current.Name, "GaoApp.Web", StringComparison.OrdinalIgnoreCase)
                && File.Exists(currentAppSettings))
            {
                return current.FullName;
            }

            var webProject = Path.Combine(current.FullName, "GaoApp.Web");
            if (File.Exists(Path.Combine(webProject, "appsettings.json")))
            {
                return webProject;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Không tìm thấy thư mục GaoApp.Web chứa appsettings.json từ thư mục hiện tại.");
    }

    /// <summary>
    /// CurrentUser giả cho EF design-time.
    /// </summary>
    private sealed class DesignTimeCurrentUser : ICurrentUser
    {
        public int? UserId => null;
        public string? UserName => null;
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => false;
    }
}
