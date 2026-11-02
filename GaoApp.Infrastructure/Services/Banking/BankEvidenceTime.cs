using System.Globalization;
using System.Text.RegularExpressions;
using GaoApp.Domain.Entities;

namespace GaoApp.Infrastructure.Services.Banking;

public static class BankEvidenceTime
{
    public static (BankTimePrecision Precision, DateTime? BusinessDate, DateTime? OccurredAtUtc) Parse(string? raw)
    {
        if (DateTime.TryParseExact(raw, ["yyyy-MM-dd", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day.Year is >=2000 and <=9998)
            // The anchor is solely for bank-day bucketing. UI/export must label BankDay and omit its hour.
            return (BankTimePrecision.BankDay, DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified), DateTime.SpecifyKind(day.Date.AddHours(-7), DateTimeKind.Utc));
        if (raw is not null && Regex.IsMatch(raw, @"(?:Z|[+-]\d{2}:?\d{2})$") &&
            DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) && at.UtcDateTime.Year is >=2000 and <=9998)
            return (BankTimePrecision.Timestamp, at.ToOffset(TimeSpan.FromHours(7)).Date, at.UtcDateTime);
        return (BankTimePrecision.Unknown, null, null);
    }
}
