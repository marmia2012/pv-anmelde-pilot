namespace PvAutopilot.Core.Processes;

/// <summary>Fachliche Datumswerte (Inbetriebnahme, Zusage …) gelten in deutscher Zeit, nicht in UTC.</summary>
public static class ProcessCalendar
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(ToBerlin(time.GetUtcNow()).DateTime);

    public static DateTimeOffset ToBerlin(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Berlin);
}
