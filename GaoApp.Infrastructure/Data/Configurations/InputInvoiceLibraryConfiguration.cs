using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class InputInvoiceLibraryEntryConfiguration : IEntityTypeConfiguration<InputInvoiceLibraryEntry>
{
    public void Configure(EntityTypeBuilder<InputInvoiceLibraryEntry> b)
    {
        b.ToTable("InputInvoiceLibraryEntries");
        b.HasIndex(x => new { x.StoreId, x.IdentityHash }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.InvoiceDate, x.Id });
        b.Property(x => x.BeforeTax).HasPrecision(18, 2);
        b.Property(x => x.Tax).HasPrecision(18, 2);
        b.Property(x => x.Total).HasPrecision(18, 2);
        b.HasMany(x => x.Reviews).WithOne(x => x.Entry).HasForeignKey(x => x.InputInvoiceLibraryEntryId).OnDelete(DeleteBehavior.Restrict);
    }
}
