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

        var nextLocal = nextLocalDate + clinic.SendWindowStart.ToTimeSpan();
        if (clinic.SendWindowStart > clinic.SendWindowEnd && localTime < clinic.SendWindowStart && localTime >= clinic.SendWindowEnd)
            nextLocal = nextLocalDate + clinic.SendWindowStart.ToTimeSpan();

        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified), zone);
        return new SendWindowDecision(false, nextUtc, zone.Id);
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

    private static TimeZoneInfo ResolveZone(string? timeZoneId)
    {
        var id = string.IsNullOrWhiteSpace(timeZoneId) ? "Europe/Istanbul" : timeZoneId.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
    }
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
