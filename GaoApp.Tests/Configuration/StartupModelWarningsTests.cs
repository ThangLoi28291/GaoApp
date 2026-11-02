using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Conversions;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Configuration;

public sealed class StartupModelWarningsTests
{
    [Fact]
    public void Sql_server_model_builds_without_validation_warnings()
    {
        var warnings = new List<string>();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseGaoAppSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=MetadataOnly;Integrated Security=True")
            .EnableServiceProviderCaching(false)
            .LogTo(warnings.Add, LogLevel.Warning)
            .Options;
        using var db = CreateContext(options);

        _ = db.Model;

        Assert.Empty(warnings);
    }

    public static TheoryData<Type, string> MoneyKeyProperties => new()
    {
        { typeof(StorefrontQuote), "GoodsAmount" },
        { typeof(StorefrontQuote), "ShippingCharge" },
        { typeof(StorefrontQuote), "TotalDue" },
        { typeof(StorefrontOrder), "GoodsAmount" },
        { typeof(StorefrontOrder), "ShippingCharge" },
        { typeof(StorefrontOrder), "TotalDue" },
        { typeof(StorefrontPaymentIntent), "AmountDue" },
        { typeof(StorefrontBankReceipt), "Amount" },
        { typeof(StorefrontPaymentAllocation), "Amount" }
    };

    [Theory, MemberData(nameof(MoneyKeyProperties))]
    public void Money_keys_keep_decimal_SQL_storage_and_reject_fractional_parameters(Type type, string name)
    {
        using var db = CreateContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseGaoAppSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=MetadataOnly;Integrated Security=True")
            .Options);
        var property = db.Model.FindEntityType(type)!.FindProperty(name)!;
        var mapping = property.GetRelationalTypeMapping();

        Assert.Equal("decimal(18,2)", mapping.StoreType);
        Assert.Equal(typeof(decimal), property.ClrType);
        var converter = Assert.IsAssignableFrom<Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter>(mapping.Converter);
        Assert.Equal(typeof(decimal), converter.ProviderClrType);
        Assert.Equal(230_000m, converter.ConvertToProvider(230_000m));
        Assert.Equal(230_000m, converter.ConvertFromProvider(230_000m));
        Assert.Equal(WholeVndKeyConverter.MaximumAmount, converter.ConvertToProvider(WholeVndKeyConverter.MaximumAmount));
        Assert.Throws<InvalidOperationException>(() => converter.ConvertToProvider(230_000.01m));
        Assert.Throws<InvalidOperationException>(() => converter.ConvertToProvider(WholeVndKeyConverter.MaximumAmount + 1m));
    }

    [Fact]
    public async Task Route_history_returns_the_same_records_with_and_without_required_navigation()
    {
        await using var db = CreateContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("startup-route-filter-" + Guid.NewGuid())
            .Options);
        var active = new AcbCallbackRoute { Host = "active.example", RowVersion = new byte[8] };
        var deleted = new AcbCallbackRoute { Host = "deleted.example", RowVersion = new byte[8] };
        db.AddRange(
            new AcbCallbackRouteChange { Route = active, ActorUserId = 1, ChangedAtUtc = DateTime.UtcNow },
            new AcbCallbackRouteChange { Route = deleted, ActorUserId = 1, ChangedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        // Set the stored soft-delete flag without severing the required
        // history relationship in the change tracker.
        deleted.IsDeleted = true;
        await db.SaveChangesAsync();
        Assert.True(deleted.IsDeleted);
        db.ChangeTracker.Clear();

        var direct = await db.Set<AcbCallbackRouteChange>().ToListAsync();
        var included = await db.Set<AcbCallbackRouteChange>().Include(x => x.Route).ToListAsync();

        Assert.Equal(Assert.Single(direct).Id, Assert.Single(included).Id);
        Assert.Equal("active.example", included[0].Route.Host);
        Assert.Equal(2, await db.Set<AcbCallbackRouteChange>().IgnoreQueryFilters().CountAsync());
    }

    private static AppDbContext CreateContext(DbContextOptions<AppDbContext> options)
    {
        var tenant = new TenantContext();
        tenant.SetHostAdmin();
        tenant.SetAuthenticatedHostAdmin(true);
        return new AppDbContext(options, tenant, new TestUser());
    }

    private sealed class TestUser : ICurrentUser
    {
        public int? UserId => 1;
        public string? UserName => "startup-model-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
