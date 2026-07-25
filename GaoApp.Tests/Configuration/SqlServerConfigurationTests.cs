using GaoApp.Application.Common.Interfaces;
using GaoApp.Infrastructure;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GaoApp.Tests.Configuration;

public sealed class SqlServerConfigurationTests
{
    private const string TestConnectionString =
        "Server=127.0.0.1,1;Database=R14SqlPolicy;" +
        "Integrated Security=True;TrustServerCertificate=True;Connect Timeout=1";

    [Fact]
    public void AppDbContext_UsesExpectedNonRetryingSqlStrategy()
    {
        using var context = CreateAppDbContext();

        var strategy = context.Database.CreateExecutionStrategy();

        Assert.False(strategy.RetriesOnFailure);
        Assert.False(SqlServerConfiguration.AutomaticRetryEnabled);
    }

    [Fact]
    public void AuditLogDbContext_UsesSameSqlStrategy()
    {
        using var appContext = CreateAppDbContext();
        using var auditContext = CreateAuditLogDbContext();

        var appStrategy = appContext.Database.CreateExecutionStrategy();
        var auditStrategy = auditContext.Database.CreateExecutionStrategy();

        Assert.Equal(appStrategy.GetType(), auditStrategy.GetType());
        Assert.Equal(
            appStrategy.RetriesOnFailure,
            auditStrategy.RetriesOnFailure);
        Assert.False(auditStrategy.RetriesOnFailure);
    }

    [Fact]
    public void RuntimeRegistrations_UseSameNonRetryingSqlStrategy()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    TestConnectionString
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var appContext =
            scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AuditLogDbContext>>();
        using var auditContext = auditFactory.CreateDbContext();

        var appStrategy = appContext.Database.CreateExecutionStrategy();
        var auditStrategy = auditContext.Database.CreateExecutionStrategy();

        Assert.False(appStrategy.RetriesOnFailure);
        Assert.False(auditStrategy.RetriesOnFailure);
        Assert.Equal(appStrategy.GetType(), auditStrategy.GetType());
    }

    [Fact]
    public void RetryLimits_AreFiniteAndDisabled()
    {
        Assert.Equal(0, SqlServerConfiguration.MaximumRetryCount);
        Assert.Equal(TimeSpan.Zero, SqlServerConfiguration.MaximumRetryDelay);
    }

    [Fact]
    public async Task FinalException_IsPropagatedWithoutReplayingSideEffects()
    {
        using var context = CreateAppDbContext();
        var strategy = context.Database.CreateExecutionStrategy();
        var expected = new InvalidOperationException("terminal SQL operation");
        var attempts = 0;
        var externalSideEffects = 0;

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteAsync(async () =>
            {
                attempts++;
                externalSideEffects++;
                await Task.Yield();
                throw expected;
            }));

        Assert.Same(expected, actual);
        Assert.Equal(1, attempts);
        Assert.Equal(1, externalSideEffects);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidSqlConfiguration_FailsBeforeAnyDatabaseOperation(
        string? connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();

        Assert.ThrowsAny<ArgumentException>(
            () => options.UseGaoAppSqlServer(connectionString));
    }

    [Fact]
    public async Task ConfigurationFailure_IsNotRetried()
    {
        using var context = CreateAppDbContext();
        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;

        await Assert.ThrowsAsync<ArgumentException>(
            () => strategy.ExecuteAsync(() =>
            {
                attempts++;
                throw new ArgumentException("invalid SQL configuration");
            }));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void ConfiguredStrategy_IsCompatibleWithExistingManualTransactions()
    {
        using var context = CreateAppDbContext();

        // EF Core rejects user-initiated transactions only when the configured
        // execution strategy retries. The actual provider strategy is checked
        // here rather than asserting source text.
        Assert.False(context.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    private static AppDbContext CreateAppDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseGaoAppSqlServer(TestConnectionString)
            .Options;

        var tenant = new TenantContext();
        tenant.SetHostAdmin();

        return new AppDbContext(
            options,
            tenant,
            new TestCurrentUser());
    }

    private static AuditLogDbContext CreateAuditLogDbContext()
    {
        var options = new DbContextOptionsBuilder<AuditLogDbContext>()
            .UseGaoAppSqlServer(TestConnectionString)
            .Options;

        return new AuditLogDbContext(options);
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => null;
        public string? UserName => null;
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => false;
    }
}
