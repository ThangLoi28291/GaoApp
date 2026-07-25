using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using GaoApp.Web.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Configuration;

public sealed class DatabaseHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthyWhenDatabaseConnects()
    {
        var probe = new FakeProbe(_ => Task.FromResult(true));
        var check = CreateCheck(probe);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, probe.CallCount);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthyWhenDatabaseCannotConnect()
    {
        var probe = new FakeProbe(_ => Task.FromResult(false));
        var check = CreateCheck(probe);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
        Assert.Equal(1, probe.CallCount);
    }

    [Fact]
    public async Task CheckHealthAsync_timeout_should_cancel_probe()
    {
        var probe = new LifecycleProbe(TimeSpan.Zero);
        var check = CreateCheck(
            probe,
            timeout: TimeSpan.FromMilliseconds(50));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Database readiness check timed out.", result.Description);
        Assert.Null(result.Exception);
        Assert.True(probe.LastToken.CanBeCanceled);
        Assert.True(probe.LastToken.IsCancellationRequested);
        Assert.True(probe.CleanupCompleted);
    }

    [Fact]
    public async Task CheckHealthAsync_timeout_should_wait_until_probe_cleanup_finishes()
    {
        var cleanupDelay = TimeSpan.FromMilliseconds(120);
        var probe = new LifecycleProbe(cleanupDelay);
        var check = CreateCheck(
            probe,
            timeout: TimeSpan.FromMilliseconds(40));
        var stopwatch = Stopwatch.StartNew();

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        stopwatch.Stop();

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(0, probe.ActiveOperations);
        Assert.True(probe.CleanupCompleted);
        Assert.True(
            stopwatch.Elapsed >= cleanupDelay,
            $"Health check returned before cleanup delay: {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task CheckHealthAsync_should_not_leave_probe_running_after_timeout_result()
    {
        var probe = new LifecycleProbe(TimeSpan.FromMilliseconds(80));
        var check = CreateCheck(
            probe,
            timeout: TimeSpan.FromMilliseconds(30));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(0, probe.ActiveOperations);
        Assert.Equal(probe.Started, probe.Completed);
        Assert.True(probe.OperationTask?.IsCompleted);
    }

    [Fact]
    public async Task CheckHealthAsync_caller_cancellation_should_propagate()
    {
        var probe = new LifecycleProbe(TimeSpan.FromMilliseconds(75));
        var check = CreateCheck(probe);
        using var cancellation = new CancellationTokenSource();
        var checkTask = check.CheckHealthAsync(
            new HealthCheckContext(),
            cancellation.Token);
        await probe.StartedSignal.Task;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => checkTask);

        Assert.True(probe.LastToken.IsCancellationRequested);
        Assert.True(probe.CleanupCompleted);
        Assert.Equal(0, probe.ActiveOperations);
        Assert.Equal(probe.Started, probe.Completed);
    }

    [Fact]
    public async Task CheckHealthAsync_SanitizesProviderExceptionAndLog()
    {
        const string secret =
            "Server=prod-db;Database=secret-db;User Id=admin;Password=top-secret";
        var logger = new CapturingLogger<DatabaseHealthCheck>();
        var probe = new FakeProbe(
            _ => Task.FromException<bool>(
                new InvalidOperationException(secret)));
        var check = CreateCheck(probe, logger: logger);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
        Assert.DoesNotContain(secret, result.Description ?? string.Empty);
        Assert.DoesNotContain(secret, logger.Messages);
        Assert.Contains(
            logger.Messages,
            message => message.Contains(
                nameof(InvalidOperationException),
                StringComparison.Ordinal));
        Assert.All(logger.Exceptions, Assert.Null);
    }

    [Fact]
    public async Task ProductionHealthResponse_DoesNotExposeInfrastructureDetails()
    {
        const string secret =
            "Server=prod-db;Database=secret-db;User Id=admin;Password=top-secret";
        var entry = new HealthReportEntry(
            HealthStatus.Unhealthy,
            secret,
            TimeSpan.FromMilliseconds(10),
            new InvalidOperationException(secret),
            new Dictionary<string, object>
            {
                ["connectionString"] = secret
            });
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["database"] = entry
            },
            TimeSpan.FromMilliseconds(10));

        var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(
                new TestHostEnvironment(Environments.Production))
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await HealthCheckResponseWriter.WriteResponseAsync(context, report);

        responseBody.Position = 0;
        var body = await new StreamReader(
            responseBody,
            Encoding.UTF8,
            leaveOpen: true).ReadToEndAsync();

        Assert.Contains("\"status\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("prod-db", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-db", body, StringComparison.Ordinal);
        Assert.DoesNotContain("admin", body, StringComparison.Ordinal);
    }

    [Fact]
    public void HealthCheckTimeoutBounds_AreFinite()
    {
        Assert.True(DatabaseHealthCheckOptions.DefaultTimeout > TimeSpan.Zero);
        Assert.True(
            DatabaseHealthCheckOptions.DefaultTimeout <=
            DatabaseHealthCheckOptions.MaximumTimeout);
        Assert.True(
            DatabaseHealthCheckOptions.MaximumTimeout <=
            TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task DatabaseConnectionProbe_applies_canonical_provider_timeout()
    {
        const string configuredConnectionString =
            "Server=127.0.0.1,1;Database=R14Probe;" +
            "Integrated Security=True;Connect Timeout=29";
        var configuration = CreateConfiguration(configuredConnectionString);
        var factory = new CapturingConnectionFactory();
        var probe = CreateDatabaseConnectionProbe(
            configuration,
            factory,
            TimeSpan.FromMilliseconds(1200));

        var canConnect = await probe.CanConnectAsync(CancellationToken.None);

        Assert.True(canConnect);
        Assert.Equal(1, factory.CreateCount);
        Assert.NotNull(factory.LastConnectionString);
        var applied =
            new SqlConnectionStringBuilder(factory.LastConnectionString);
        Assert.Equal(2, applied.ConnectTimeout);
        Assert.InRange(
            applied.ConnectTimeout,
            1,
            (int)DatabaseHealthCheckOptions.MaximumTimeout.TotalSeconds);
        Assert.Equal(
            configuredConnectionString,
            configuration.GetConnectionString("DefaultConnection"));
        Assert.True(factory.LastConnection!.WasDisposed);
    }

    [Fact]
    public async Task DatabaseConnectionProbe_timeout_waits_for_connection_cleanup_and_disposal()
    {
        var activeOperations = 0;
        var cleanupCompleted = false;
        var factory = new CapturingConnectionFactory(async token =>
        {
            activeOperations++;

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                cleanupCompleted = true;
                throw;
            }
            finally
            {
                activeOperations--;
            }
        });
        var timeout = TimeSpan.FromMilliseconds(40);
        var probe = CreateDatabaseConnectionProbe(
            CreateConfiguration(
                "Server=127.0.0.1,1;Database=R14Probe;" +
                "Integrated Security=True"),
            factory,
            timeout);
        var check = CreateCheck(probe, timeout);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.True(cleanupCompleted);
        Assert.Equal(0, activeOperations);
        Assert.True(factory.LastConnection!.WasDisposed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DatabaseConnectionProbe_blank_connection_string_fails_before_open(
        string? connectionString)
    {
        var factory = new CapturingConnectionFactory();

        var exception = Assert.Throws<InvalidOperationException>(
            () => CreateDatabaseConnectionProbe(
                CreateConfiguration(connectionString),
                factory,
                DatabaseHealthCheckOptions.DefaultTimeout));

        Assert.Contains(
            "ConnectionStrings:DefaultConnection",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public void DatabaseConnectionProbe_invalid_connection_string_is_sanitized()
    {
        const string secret =
            "Server=secret-host;Password=top-secret;Invalid Keyword=value";
        var factory = new CapturingConnectionFactory();

        var exception = Assert.Throws<InvalidOperationException>(
            () => CreateDatabaseConnectionProbe(
                CreateConfiguration(secret),
                factory,
                DatabaseHealthCheckOptions.DefaultTimeout));

        Assert.Contains(
            "ConnectionStrings:DefaultConnection",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("secret-host", exception.Message);
        Assert.DoesNotContain("top-secret", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(0, factory.CreateCount);
    }

    private static DatabaseHealthCheck CreateCheck(
        IDatabaseConnectionProbe probe,
        TimeSpan? timeout = null,
        ILogger<DatabaseHealthCheck>? logger = null)
    {
        var options = Options.Create(new DatabaseHealthCheckOptions
        {
            Timeout = timeout ?? DatabaseHealthCheckOptions.DefaultTimeout
        });

        return new DatabaseHealthCheck(
            probe,
            logger ?? new CapturingLogger<DatabaseHealthCheck>(),
            options);
    }

    private static DatabaseConnectionProbe CreateDatabaseConnectionProbe(
        IConfiguration configuration,
        IDatabaseConnectionFactory connectionFactory,
        TimeSpan timeout)
        => new(
            configuration,
            Options.Create(new DatabaseHealthCheckOptions
            {
                Timeout = timeout
            }),
            connectionFactory);

    private static IConfiguration CreateConfiguration(
        string? connectionString)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString
            })
            .Build();

    private sealed class FakeProbe : IDatabaseConnectionProbe
    {
        private readonly Func<CancellationToken, Task<bool>> _operation;

        public FakeProbe(Func<CancellationToken, Task<bool>> operation)
        {
            _operation = operation;
        }

        public int CallCount { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<bool> CanConnectAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            LastToken = cancellationToken;
            return _operation(cancellationToken);
        }
    }

    private sealed class LifecycleProbe : IDatabaseConnectionProbe
    {
        private readonly TimeSpan _cleanupDelay;

        public LifecycleProbe(TimeSpan cleanupDelay)
        {
            _cleanupDelay = cleanupDelay;
        }

        public int ActiveOperations { get; private set; }
        public int Started { get; private set; }
        public int Completed { get; private set; }
        public bool CleanupCompleted { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public Task? OperationTask { get; private set; }
        public TaskCompletionSource StartedSignal { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> CanConnectAsync(CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            OperationTask = RunAsync(cancellationToken);
            return (Task<bool>)OperationTask;
        }

        private async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            Started++;
            ActiveOperations++;
            StartedSignal.TrySetResult();

            try
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
                return false;
            }
            catch (OperationCanceledException)
            {
                await Task.Delay(_cleanupDelay);
                CleanupCompleted = true;
                throw;
            }
            finally
            {
                ActiveOperations--;
                Completed++;
            }
        }
    }

    private sealed class CapturingConnectionFactory
        : IDatabaseConnectionFactory
    {
        private readonly Func<CancellationToken, Task>? _openOperation;

        public CapturingConnectionFactory(
            Func<CancellationToken, Task>? openOperation = null)
        {
            _openOperation = openOperation;
        }

        public int CreateCount { get; private set; }
        public string? LastConnectionString { get; private set; }
        public StubDbConnection? LastConnection { get; private set; }

        public DbConnection Create(string connectionString)
        {
            CreateCount++;
            LastConnectionString = connectionString;
            LastConnection = new StubDbConnection(
                connectionString,
                _openOperation);
            return LastConnection;
        }
    }

    private sealed class StubDbConnection : DbConnection
    {
        private readonly Func<CancellationToken, Task>? _openOperation;
        private ConnectionState _state = ConnectionState.Closed;

        public StubDbConnection(
            string connectionString,
            Func<CancellationToken, Task>? openOperation)
        {
            ConnectionString = connectionString;
            _openOperation = openOperation;
        }

        public bool WasDisposed { get; private set; }
        [AllowNull]
        public override string ConnectionString { get; set; }
        public override string Database => "R14Probe";
        public override string DataSource => "synthetic";
        public override string ServerVersion => "test";
        public override ConnectionState State => _state;

        public override async Task OpenAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_openOperation is not null)
            {
                await _openOperation(cancellationToken);
            }

            _state = ConnectionState.Open;
        }

        public override void Open()
        {
            _state = ConnectionState.Open;
        }

        public override void Close()
        {
            _state = ConnectionState.Closed;
        }

        public override void ChangeDatabase(string databaseName)
        {
        }

        protected override DbTransaction BeginDbTransaction(
            IsolationLevel isolationLevel)
            => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand()
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            _state = ConnectionState.Closed;
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();
        public List<Exception?> Exceptions { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose()
        {
        }
    }

    private sealed class TestHostEnvironment : IWebHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string ApplicationName { get; set; } = "GaoApp.Tests";
        public IFileProvider WebRootFileProvider { get; set; } =
            new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; }
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
