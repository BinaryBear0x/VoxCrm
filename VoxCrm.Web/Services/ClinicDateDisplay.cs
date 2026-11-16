namespace VoxCrm.Web.Services;

public static class ClinicDateDisplay
{
    // Legacy timestamp mappings can materialize Local values; unspecified database dates represent UTC.
    public static DateTime Convert(DateTime value, TimeZoneInfo zone)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
    }
}
