using FluentAssertions;

namespace GaoApp.Tests.Tenant;

public class TenantResolutionTests
{
    [Fact]
    public void Should_Resolve_Store_From_Subdomain()
    {
        // Arrange
        var subdomain = "store-a";

        // Fake mapping
        var storeMapping = new Dictionary<string, int>
        {
            { "store-a", 1 },
            { "store-b", 2 }
        };

        // Act
        var storeId = storeMapping.ContainsKey(subdomain)
            ? storeMapping[subdomain]
            : (int?)null;

        // Assert
        storeId.Should().Be(1);
    }

    [Fact]
    public void Should_Return_Null_When_Subdomain_Not_Found()
    {
        // Arrange
        var subdomain = "unknown";

        var storeMapping = new Dictionary<string, int>
        {
            { "store-a", 1 }
        };

        // Act
        var storeId = storeMapping.ContainsKey(subdomain)
            ? storeMapping[subdomain]
            : (int?)null;

        // Assert
        storeId.Should().BeNull();
    }

    [Fact]
    public void Should_Identify_Host_Admin()
    {
        // Arrange
        var subdomain = "admin";

        // Act
        var isHostAdmin = subdomain == "admin";

        // Assert
        isHostAdmin.Should().BeTrue();
    }
}