using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Common;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GaoApp.Tests.Data;

/// <summary>
/// AppDbContext dành riêng cho EF InMemory trong test.
/// SQL Server tự sinh rowversion; InMemory không có cơ chế tương đương và
/// còn bỏ qua giá trị được gán khi property là ValueGeneratedOnAddOrUpdate.
/// </summary>
internal sealed class InMemoryAppDbContext : AppDbContext
{
    public InMemoryAppDbContext(
        DbContextOptions<InMemoryAppDbContext> options,
        ITenantContext tenant,
        ICurrentUser currentUser)
        : base(options, tenant, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        var baseEntityTypes = builder.Model
            .GetEntityTypes()
            .Where(x => typeof(BaseEntity).IsAssignableFrom(x.ClrType))
            .Select(x => x.ClrType)
            .ToList();

        foreach (var entityType in baseEntityTypes)
        {
            builder.Entity(entityType)
                .Property(nameof(BaseEntity.RowVersion))
                .IsRequired(false)
                .IsConcurrencyToken(false)
                .ValueGeneratedNever();
        }
    }

    internal void VerifyRowVersionConfiguration()
    {
        var invalidProperties = Model
            .GetEntityTypes()
            .Where(x => typeof(BaseEntity).IsAssignableFrom(x.ClrType))
            .Select(x => x.FindProperty(nameof(BaseEntity.RowVersion)))
            .Where(x =>
                x == null ||
                !x.IsNullable ||
                x.IsConcurrencyToken ||
                x.ValueGenerated != ValueGenerated.Never)
            .ToList();

        if (invalidProperties.Count > 0)
        {
            throw new InvalidOperationException(
                "InMemory test model chưa vô hiệu hóa SQL Server rowversion.");
        }
    }
}
