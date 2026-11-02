namespace GaoApp.Application.Common.Helpers;

/// <summary>Vietnam business wall time; stored instants and UTC API values remain UTC.</summary>
public static class VietnamTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Asia/Ho_Chi_Minh", Offset, "Vietnam", "Vietnam");
    public static DateTime Now => DateTime.UtcNow.ToVietnamTime();
    public static DateTime Today => Now.Date;
    public static DateTime ToUtc(DateTime localWallTime) => DateTime.SpecifyKind(localWallTime.Subtract(Offset), DateTimeKind.Utc);

    // SQL datetime values have Unspecified Kind but UTC timestamp fields represent UTC.
    public static DateTime ToVietnamTime(this DateTime utc) => DateTime.SpecifyKind(
        (utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc).Add(Offset), DateTimeKind.Unspecified);
    public static DateTimeOffset ToVietnamTime(this DateTimeOffset instant) => instant.ToOffset(Offset);
}
