using GaoApp.Application.Common.Interfaces;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Payments;

public sealed class AcbSqlLockTests
{
    [Fact]
    public async Task SQL_decimal_materialization_matches_POS_fingerprint_without_changing_values()
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(9000 AS decimal(18,2)), CAST(1 AS decimal(18,4)), CAST(0 AS decimal(18,2))";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var money = reader.GetDecimal(0);
        var quantity = reader.GetDecimal(1);
        var zero = reader.GetDecimal(2);
        var order = new GaoApp.Domain.Entities.Order
        {
            GrandTotal = money, POSShiftId = 1,
            Lines = [new GaoApp.Domain.Entities.OrderLine { Id = 50, VariantId = 12,
                Quantity = quantity, UnitPrice = money, LineDiscount = zero, LineTotal = money }]
        };
        Assert.True(AcbPaymentPolicy.MatchesFingerprint(order,
            "97A15E80CF723F7D8497EE4577AA968EA5EB7FEC326DFF75E1DFDACBF7A6FBE5"));
        var fingerprint = AcbPaymentPolicy.Fingerprint(order);
        order.GrandTotal = decimal.Round(order.GrandTotal, 0);
        order.Lines.Single().LineTotal = decimal.Round(order.Lines.Single().LineTotal, 0);
        Assert.Equal(fingerprint, AcbPaymentPolicy.Fingerprint(order));
    }

    [Fact]
    public async Task Two_connections_cannot_process_the_same_store_order_at_once()
    {
        var tenant = new TenantContext();
        tenant.SetStore(Random.Shared.Next(1000000, int.MaxValue), "acb-lock-test");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString()).Options;
        await using var firstDb = new AppDbContext(options, tenant, new User());
        await using var secondDb = new AppDbContext(options, tenant, new User());
        var first = await AcbOrderLock.AcquireAsync(firstDb, 1, default);
        var secondTask = AcbOrderLock.AcquireAsync(secondDb, 1, default);
        try
        {
            await Task.Delay(150);
            Assert.False(secondTask.IsCompleted);
        }
        finally { await first.DisposeAsync(); }
        await using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(10));
    }
    [Fact]
    public async Task Releasing_nested_order_lock_keeps_callback_receipt_locked_until_processing_finishes()
    {
        var tenant = new TenantContext();
        tenant.SetStore(Random.Shared.Next(1000000, int.MaxValue), "acb-nested-lock-test");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString()).Options;
        await using var firstDb = new AppDbContext(options, tenant, new User());
        await using var secondDb = new AppDbContext(options, tenant, new User());
        var receiptLock = await AcbOrderLock.AcquireAsync(firstDb, -1, default);
        try
        {
            var orderLock = await AcbOrderLock.AcquireAsync(firstDb, 1, default);
            await orderLock.DisposeAsync();
            var competing = AcbOrderLock.AcquireAsync(secondDb, -1, default);
            try
            {
                await Task.Delay(150);
                Assert.False(competing.IsCompleted);
            }
            finally
            {
                await receiptLock.DisposeAsync();
                receiptLock = null!;
                await using var acquired = await competing.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        finally { if (receiptLock != null) await receiptLock.DisposeAsync(); }
    }

    internal static string ConnectionString() => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
    {
        DataSource = Environment.GetEnvironmentVariable("GAOAPP_ACB_SQL_TEST_SERVER") ?? SqlTestDataSource.Current,
        InitialCatalog = "master", IntegratedSecurity = true, TrustServerCertificate = true,
        ConnectTimeout = 5, Pooling = false
    }.ConnectionString;

    private sealed class User : ICurrentUser
    {
        public int? UserId => null; public string? UserName => null;
        public int? TerminalId => null; public string? TerminalCode => null; public bool IsAuthenticated => false;
    }
}
