using System.Globalization;

namespace CaneFactory.Application.Utilities;

/// <summary>One formatting boundary for all user-facing Indian factory times.
/// SQL Server returns UTC columns as DateTimeKind.Unspecified, so those values
/// are explicitly treated as UTC before converting to India Standard Time.</summary>
public static class IndiaTime
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    public static DateTime ToIst(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
    }

    public static string Format(DateTime value) =>
        ToIst(value).ToString("dd-MM-yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    private static TimeZoneInfo ResolveZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
    }
}
