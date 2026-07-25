using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.HealthChecks;

public interface IDatabaseConnectionProbe
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}

public interface IDatabaseConnectionFactory
{
    DbConnection Create(string connectionString);
}

public sealed class SqlDatabaseConnectionFactory : IDatabaseConnectionFactory
{
    public DbConnection Create(string connectionString)
        => new SqlConnection(connectionString);
}

public sealed class DatabaseConnectionProbe : IDatabaseConnectionProbe
{
    private const string ConnectionStringKey =
        "ConnectionStrings:DefaultConnection";

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly string _connectionString;

    public DatabaseConnectionProbe(
        IConfiguration configuration,
        IOptions<DatabaseHealthCheckOptions> options,
        IDatabaseConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        var configuredConnectionString =
            configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringKey} chưa được cấu hình hoặc đang rỗng.");
        }

        _connectionFactory = connectionFactory;
        _connectionString = BuildProbeConnectionString(
            configuredConnectionString,
            options.Value.Timeout);
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        await using var connection =
            _connectionFactory.Create(_connectionString);

        await connection.OpenAsync(cancellationToken);
        return true;
    }

    private static string BuildProbeConnectionString(
        string configuredConnectionString,
        TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero ||
            timeout > DatabaseHealthCheckOptions.MaximumTimeout)
        {
            throw new InvalidOperationException(
                "Database health check timeout không hợp lệ.");
        }

        try
        {
            var builder =
                new SqlConnectionStringBuilder(configuredConnectionString);

            // SqlClient accepts whole seconds. Ceiling adds less than one
            // second, keeps the minimum valid, and never exceeds 30 seconds.
            builder.ConnectTimeout = Math.Clamp(
                (int)Math.Ceiling(timeout.TotalSeconds),
                1,
                (int)DatabaseHealthCheckOptions.MaximumTimeout.TotalSeconds);

            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(
                $"{ConnectionStringKey} không hợp lệ.");
        }
    }
}
