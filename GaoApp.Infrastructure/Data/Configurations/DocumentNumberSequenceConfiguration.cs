using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class DocumentNumberSequenceConfiguration : IEntityTypeConfiguration<DocumentNumberSequence>
{
    public void Configure(EntityTypeBuilder<DocumentNumberSequence> builder)
    {
        builder.ToTable("DocumentNumberSequences");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SequenceDate)
            .HasColumnType("date");

        builder.Property(x => x.LastNumber)
            .HasDefaultValue(0);

        // Mỗi store + loại sequence + ngày chỉ có 1 dòng
        builder.HasIndex(x => new
        {
            x.StoreId,
            x.SequenceType,
            x.SequenceDate
        }).IsUnique();

    
    }
}