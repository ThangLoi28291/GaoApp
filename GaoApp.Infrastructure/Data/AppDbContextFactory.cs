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
    public AppDbContext CreateDbContext(string[] args)
    {
        // Khi chạy từ project Infrastructure, base path này trỏ về Web project
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "GaoApp.Web");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
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