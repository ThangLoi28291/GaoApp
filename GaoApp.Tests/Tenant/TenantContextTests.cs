using FluentAssertions;

namespace GaoApp.Tests.Tenant;

public class TenantContextTests
{
    [Fact]
    public void Should_Set_And_Get_Tenant_Correctly()
    {
        // Arrange
        var context = new FakeTenantContext();

        // Act
        context.StoreId = 10;
        context.Subdomain = "store-a";
        context.IsHostAdmin = false;

        // Assert
        context.StoreId.Should().Be(10);
        context.Subdomain.Should().Be("store-a");
        context.IsHostAdmin.Should().BeFalse();
    }

    [Fact]
    public void Should_Be_HostAdmin_When_Set()
    {
        // Arrange
        var context = new FakeTenantContext();

        // Act
        context.IsHostAdmin = true;

        // Assert
        context.IsHostAdmin.Should().BeTrue();
    }
}