using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using System.Security.Claims;

namespace GaoApp.Tests.Security;

public class PolicyAuthorizationTests
{
    private static ClaimsPrincipal CreateUser(params string[] permissions)
    {
        var claims = permissions
            .Select(p => new Claim("permission", p))
            .ToList();

        var identity = new ClaimsIdentity(claims, "test");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task Should_Allow_When_User_Has_Permission()
    {
        // Arrange
        var requirement = new ClaimsAuthorizationRequirement(
            "permission",
            new[] { "CanManageCatalog" });

        var user = CreateUser("CanManageCatalog");

        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            user,
            null);

        var handler = new PassThroughAuthorizationHandler();

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Deny_When_User_Does_Not_Have_Permission()
    {
        // Arrange
        var requirement = new ClaimsAuthorizationRequirement(
            "permission",
            new[] { "CanManageCatalog" });

        var user = CreateUser("CanSell");

        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            user,
            null);

        var handler = new PassThroughAuthorizationHandler();

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }
}