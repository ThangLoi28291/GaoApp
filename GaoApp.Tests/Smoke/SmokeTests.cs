using FluentAssertions;

namespace GaoApp.Tests.Smoke;

public class SmokeTests
{
    [Fact]
    public void True_Should_Be_True()
    {
        // Arrange
        var value = true;

        // Act
        var result = value;

        // Assert
        result.Should().BeTrue();
    }
}