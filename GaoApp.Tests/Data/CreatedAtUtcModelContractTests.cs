using System.Reflection;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Data;

public class CreatedAtUtcModelContractTests
{
    public static TheoryData<Type> TargetEntityTypes => new()
    {
        typeof(MediaAsset),
        typeof(InvoiceCorrectionCase),
        typeof(SalesReturn)
    };

    [Theory]
    [MemberData(nameof(TargetEntityTypes))]
    public void Entity_should_expose_only_inherited_CreatedAtUtc(
        Type entityType)
    {
        var createdAtProperties = entityType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property =>
                property.Name == nameof(BaseEntity.CreatedAtUtc))
            .ToArray();

        var createdAtProperty = Assert.Single(createdAtProperties);

        Assert.Equal(
            typeof(BaseEntity),
            createdAtProperty.DeclaringType);
    }

    [Theory]
    [MemberData(nameof(TargetEntityTypes))]
    public void Ef_model_should_map_one_inherited_CreatedAtUtc(
        Type entityType)
    {
        using var context = CreateContext();

        var metadata = context.Model.FindEntityType(entityType);

        Assert.NotNull(metadata);

        var createdAtProperties = metadata
            .GetProperties()
            .Where(property =>
                property.Name == nameof(BaseEntity.CreatedAtUtc))
            .ToArray();

        var createdAtProperty = Assert.Single(createdAtProperties);

        Assert.Equal(typeof(DateTime), createdAtProperty.ClrType);

        Assert.Equal(
            typeof(BaseEntity),
            createdAtProperty.PropertyInfo?.DeclaringType);
    }

    [Fact]
    public async Task SaveChanges_should_set_inherited_CreatedAtUtc()
    {
        await using var context = CreateContext();

        var mediaAsset = new MediaAsset
        {
            StoreId = 1,
            StoragePath = "w4/media-test.png",
            RowVersion = new byte[8]
        };

        var correctionCase = new InvoiceCorrectionCase
        {
            StoreId = 1,
            OriginalInvoiceHeadId = 1001,
            Reason = "W4 test reason",
            AgreementDocumentNo = "W4-AGREEMENT",
            AgreementDateUtc = DateTime.UtcNow,
            RowVersion = new byte[8]
        };

        var salesReturn = new SalesReturn
        {
            StoreId = 1,
            ReturnNumber = "W4-RETURN",
            OrderId = 1001,
            POSShiftId = 1001,
            Reason = "W4 test reason",
            CreatedByUserId = 99,
            RowVersion = new byte[8]
        };

        context.AddRange(
            mediaAsset,
            correctionCase,
            salesReturn);

        await context.SaveChangesAsync();

        var savedEntities = new BaseEntity[]
        {
            mediaAsset,
            correctionCase,
            salesReturn
        };

        Assert.All(
            savedEntities,
            entity =>
            {
                Assert.NotEqual(
                    default,
                    entity.CreatedAtUtc);

                Assert.Equal(
                    99,
                    entity.CreatedBy);
            });
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(
                $"w4-created-at-{Guid.NewGuid()}")
            .Options;

        var tenant = new TenantContext();
        tenant.SetHostAdmin();

        return new AppDbContext(
            options,
            tenant,
            new TestCurrentUser());
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;

        public string? UserName => "w4-test";

        public int? TerminalId => null;

        public string? TerminalCode => null;

        public bool IsAuthenticated => true;
    }
}