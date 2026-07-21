using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Data;

public class DbContextGlobalFilterTests
{
    private class TestEntity
    {
        public int Id { get; set; }
        public int StoreId { get; set; }
        public bool IsDeleted { get; set; }
    }

    private class TestDbContext : DbContext
    {
        private readonly int _currentStoreId;

        public TestDbContext(DbContextOptions options, int currentStoreId)
            : base(options)
        {
            _currentStoreId = currentStoreId;
        }

        public DbSet<TestEntity> Entities => Set<TestEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntity>()
                .HasQueryFilter(x => x.StoreId == _currentStoreId && !x.IsDeleted);
        }
    }

    [Fact]
    public void Should_Apply_Global_Filter_Correctly()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var currentStoreId = 1;

        using (var context = new TestDbContext(options, currentStoreId))
        {
            context.Entities.AddRange(
                new TestEntity { Id = 1, StoreId = 1, IsDeleted = false },
                new TestEntity { Id = 2, StoreId = 1, IsDeleted = true },
                new TestEntity { Id = 3, StoreId = 2, IsDeleted = false }
            );

            context.SaveChanges();
        }

        // Act
        using (var context = new TestDbContext(options, currentStoreId))
        {
            var result = context.Entities.ToList();

            // Assert
            result.Should().HaveCount(1);
            result.First().Id.Should().Be(1);
        }
    }
}