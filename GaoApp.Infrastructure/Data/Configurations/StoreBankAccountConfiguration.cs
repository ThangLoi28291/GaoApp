using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class StoreBankAccountConfiguration : IEntityTypeConfiguration<StoreBankAccount>
{
    public void Configure(EntityTypeBuilder<StoreBankAccount> builder)
    {
        builder.Property(x => x.BankCode)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.BankName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.AccountNumber)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.AccountName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.ProviderCode)
            .HasMaxLength(50)
            .HasDefaultValue("LOCAL");

        builder.Property(x => x.NoteTemplate)
            .HasMaxLength(500);

        builder.Property(x => x.VietQrBankBin)
            .HasMaxLength(100);

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        builder.Property(x => x.IsDefault)
            .HasDefaultValue(false);

        builder.HasIndex(x => new { x.StoreId, x.AccountNumber })
            .IsUnique();

        builder.HasIndex(x => new { x.StoreId, x.IsDefault });

        builder.HasIndex(x => x.StoreId)
            .HasDatabaseName("UX_StoreBankAccounts_OneDefaultPerStore")
            .IsUnique()
            .HasFilter("[IsDefault] = 1 AND [IsDeleted] = 0");

        builder.HasMany(x => x.QrRequests)
            .WithOne(x => x.BankAccount)
            .HasForeignKey(x => x.BankAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
