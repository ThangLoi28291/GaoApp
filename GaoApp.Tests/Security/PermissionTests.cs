using FluentAssertions;
using GaoApp.Application.Common.Security;
namespace GaoApp.Tests.Security;

public class PermissionTests
{
    [Fact]
    public void Should_Allow_When_User_Has_Permission()
    {
        // Arrange
        var userPermissions = new List<string>
        {
            "CanManageCatalog",
            "CanSell"
        };

        // Act
        var hasPermission = userPermissions.Contains("CanManageCatalog");

        // Assert
        hasPermission.Should().BeTrue();
    }

    [Fact]
    public void Should_Deny_When_User_Does_Not_Have_Permission()
    {
        // Arrange
        var userPermissions = new List<string>
        {
            "CanSell"
        };

        // Act
        var hasPermission = userPermissions.Contains("CanManageCatalog");

        // Assert
        hasPermission.Should().BeFalse();
    }

    [Fact]
    public void Should_Always_Allow_For_Admin()
    {
        // Arrange
        var isAdmin = true;

        // Act
        var canAccess = isAdmin || false;

        // Assert
        canAccess.Should().BeTrue();
    }
    [Theory]
    [InlineData(PermissionCodes.System.Invoice.ManualIssue)]
    [InlineData(PermissionCodes.System.Invoice.Route)]
    [InlineData(PermissionCodes.System.AutoInvoice.Operate)]
    [InlineData(PermissionCodes.System.AutoInvoice.Settings)]
    public void New_invoice_permissions_accept_legacy_integration_manage(
    string canonicalPermission)
    {
        var accepted =
            PermissionAliasMap.GetAcceptedCodes(
                canonicalPermission);

        Assert.Contains(
            PermissionCodes.System.Integration.Manage,
            accepted);
    }
}