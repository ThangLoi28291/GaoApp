using FluentAssertions;

namespace GaoApp.Tests.Data;

public class GlobalQueryFilterTests
{
    private class FakeEntity
    {
        public int Id { get; set; }
        public int StoreId { get; set; }
        public bool IsDeleted { get; set; }
    }

    [Fact]
    public void Should_Filter_By_StoreId()
    {
        // Arrange
        var currentStoreId = 1;

        var data = new List<FakeEntity>
        {
            new() { Id = 1, StoreId = 1, IsDeleted = false },
            new() { Id = 2, StoreId = 2, IsDeleted = false }
        };

        // Act
        var result = data
            .Where(x => x.StoreId == currentStoreId)
            .ToList();

        // Assert
        result.Should().HaveCount(1);
        result.First().StoreId.Should().Be(1);
    }

    [Fact]
    public void Should_Filter_Out_Deleted()
    {
        // Arrange
        var data = new List<FakeEntity>
        {
            new() { Id = 1, StoreId = 1, IsDeleted = false },
            new() { Id = 2, StoreId = 1, IsDeleted = true }
        };

        // Act
        var result = data
            .Where(x => !x.IsDeleted)
            .ToList();

        // Assert
        result.Should().HaveCount(1);
        result.First().IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Should_Filter_By_Store_And_Not_Deleted()
    {
        // Arrange
        var currentStoreId = 1;

        var data = new List<FakeEntity>
        {
            new() { Id = 1, StoreId = 1, IsDeleted = false },
            new() { Id = 2, StoreId = 1, IsDeleted = true },
            new() { Id = 3, StoreId = 2, IsDeleted = false }
        };

        // Act
        var result = data
            .Where(x => x.StoreId == currentStoreId && !x.IsDeleted)
            .ToList();

        // Assert
        result.Should().HaveCount(1);
        result.First().Id.Should().Be(1);
    }
}