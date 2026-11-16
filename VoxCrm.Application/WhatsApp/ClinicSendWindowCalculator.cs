using VoxCrm.Domain.Entities;

namespace VoxCrm.Application.WhatsApp;

public interface IClinicSendWindowCalculator
{
    SendWindowDecision GetDecision(ClinicSendWindowInfo clinic, DateTime utcNow);
    DateTime GetNextAllowedSendUtc(Clinic clinic, DateTime utcNow);
}

public sealed class ClinicSendWindowCalculator : IClinicSendWindowCalculator
{
    public SendWindowDecision GetDecision(ClinicSendWindowInfo clinic, DateTime utcNow)
    {
        if (!clinic.SendWindowEnabled)
            return new SendWindowDecision(true, utcNow, clinic.TimeZoneId);

        var zone = ResolveZone(clinic.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone);
        var localTime = TimeOnly.FromDateTime(localNow);

        if (IsInsideWindow(localTime, clinic.SendWindowStart, clinic.SendWindowEnd))
            return new SendWindowDecision(true, utcNow, zone.Id);

        var nextLocalDate = localNow.Date;
        if (clinic.SendWindowStart <= clinic.SendWindowEnd && localTime >= clinic.SendWindowEnd)
            nextLocalDate = nextLocalDate.AddDays(1);

        var nextLocal = DateTime.SpecifyKind(nextLocalDate + clinic.SendWindowStart.ToTimeSpan(), DateTimeKind.Unspecified);
        return new SendWindowDecision(false, LocalToUtc(nextLocal, zone), zone.Id);
    }

    // The window start can fall into a daylight-saving jump. ConvertTimeToUtc throws for a
    // local time that does not exist (spring forward) and silently picks one of two instants
    // for a time that happens twice (fall back); that exception used to fail the whole claim
    // for every clinic.
    private static DateTime LocalToUtc(DateTime local, TimeZoneInfo zone)
    {
        // The window opens at the first moment after the gap.
        for (var guard = 0; guard < 12 && zone.IsInvalidTime(local); guard++)
            local = local.AddMinutes(15);

        if (zone.IsAmbiguousTime(local))
        {
            // Open at the first occurrence (daylight time = the larger UTC offset).
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    public DateTime GetNextAllowedSendUtc(Clinic clinic, DateTime utcNow)
    {
        var decision = GetDecision(new ClinicSendWindowInfo(
            clinic.ID,
            clinic.Name,
            clinic.WhatsAppSendWindowEnabled,
            clinic.WhatsAppSendWindowStart,
            clinic.WhatsAppSendWindowEnd,
            clinic.WhatsAppTimeZoneId,
            clinic.DealerId), utcNow);

        return decision.NextAllowedUtc;
    }

    private static bool IsInsideWindow(TimeOnly current, TimeOnly start, TimeOnly end)
    {
        if (start == end) return true;
        return start < end
            ? current >= start && current < end
            : current >= start || current < end;
    }

    private static TimeZoneInfo ResolveZone(string? timeZoneId) =>
        VoxCrm.Domain.Common.ClinicTime.Zone(timeZoneId?.Trim());
}

public sealed record SendWindowDecision(bool IsOpen, DateTime NextAllowedUtc, string TimeZoneId);
public sealed record ClinicSendWindowInfo(
    Guid ClinicId,
    string ClinicName,
    bool SendWindowEnabled,
    TimeOnly SendWindowStart,
    TimeOnly SendWindowEnd,
    string TimeZoneId,
    Guid? DealerId);
