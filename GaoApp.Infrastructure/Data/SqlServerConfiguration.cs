using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Data;

/// <summary>
/// Cấu hình SQL Server dùng chung cho mọi DbContext của GaoApp.
///
/// Automatic provider retry được chủ động giữ tắt. Các workflow hiện tại có
/// transaction thủ công và audit ghi qua DbContext riêng, nên chưa an toàn để
/// replay toàn bộ đơn vị công việc bằng execution strategy.
/// </summary>
public static class SqlServerConfiguration
{
    public const bool AutomaticRetryEnabled = false;
    public const int MaximumRetryCount = 0;
    public static readonly TimeSpan MaximumRetryDelay = TimeSpan.Zero;

    public static DbContextOptionsBuilder UseGaoAppSqlServer(
        this DbContextOptionsBuilder optionsBuilder,
        string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Không bật provider retry cho tới khi toàn bộ transaction và
        // out-of-band audit persistence được thiết kế để replay an toàn.
        return optionsBuilder.UseSqlServer(connectionString);
    }

    public static DbContextOptionsBuilder<TContext> UseGaoAppSqlServer<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string? connectionString)
        where TContext : DbContext
    {
        UseGaoAppSqlServer(
            (DbContextOptionsBuilder)optionsBuilder,
            connectionString);

        return optionsBuilder;
    }
}
