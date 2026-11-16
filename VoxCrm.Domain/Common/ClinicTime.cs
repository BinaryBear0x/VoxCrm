namespace VoxCrm.Domain.Common;
public static class ClinicTime
{
    public static DateOnly Today(string? zoneId, DateTimeOffset? instant = null)
    {
        var zone = Zone(zoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant ?? DateTimeOffset.UtcNow, zone).DateTime);
    }
    public static TimeZoneInfo Zone(string? id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? "Europe/Istanbul" : id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"); }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"); }
    }
}
