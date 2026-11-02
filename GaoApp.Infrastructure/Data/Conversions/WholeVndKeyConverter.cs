using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GaoApp.Infrastructure.Data.Conversions;

/// <summary>
/// Validates whole-dong key values before SQL parameter conversion. The explicit
/// decimal(18,2) column mapping composes this with EF's long-to-decimal converter,
/// keeping the existing decimal columns, composite foreign keys and SQL checks.
/// </summary>
public sealed class WholeVndKeyConverter() : ValueConverter<decimal, long>(
    amount => ToWholeDong(amount),
    amount => (decimal)amount)
{
    public const decimal MaximumAmount = 9_999_999_999_999_999m;

    private static long ToWholeDong(decimal amount)
    {
        if (amount != decimal.Truncate(amount) || amount < -MaximumAmount || amount > MaximumAmount)
            throw new InvalidOperationException("Storefront key amounts must be whole VND within the decimal(18,2) range.");

        return decimal.ToInt64(amount);
    }
}
