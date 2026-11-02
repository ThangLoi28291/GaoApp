using GaoApp.Infrastructure.Data.Seed;

namespace GaoApp.Tests.Configuration;

public sealed class ProvisioningAuditContextTests
{
    [Fact]
    public void Bootstrap_audit_ownership_is_explicit_and_cleared_after_the_operation()
    {
        var context = new ProvisioningAuditContext();
        Assert.Null(context.GetCurrent().StoreId);
        using (context.BeginStore(23))
        {
            Assert.Equal(23, context.GetCurrent().StoreId);
            Assert.Equal("SYSTEM:migrator", context.GetCurrent().UserName);
            Assert.Null(context.GetCurrent().UserId);
            Assert.Throws<InvalidOperationException>(() => context.BeginStore(24));
        }
        Assert.Null(context.GetCurrent().StoreId);
        using (context.BeginStore(24)) Assert.Equal(24, context.GetCurrent().StoreId);
        Assert.Null(context.GetCurrent().StoreId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Bootstrap_audit_rejects_an_unknown_store(int storeId)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new ProvisioningAuditContext().BeginStore(storeId));
}
