using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class PosReceiptTemplateConfiguration : IEntityTypeConfiguration<PosReceiptTemplate>
{
    public void Configure(EntityTypeBuilder<PosReceiptTemplate> b)
    {
        b.ToTable("PosReceiptTemplates");
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.DefinitionJson).IsRequired();
        b.HasIndex(x => new { x.StoreId, x.IsDeleted });
    }
}
