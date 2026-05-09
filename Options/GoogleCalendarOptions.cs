namespace SwiftMere.Booking.Api.Options;

public sealed class GoogleCalendarOptions
{
    public const string SectionName = "GoogleCalendar";

    public string ServiceAccountEmail { get; init; } = string.Empty;
    public string PrivateKey { get; init; } = string.Empty;
    public string CalendarId { get; init; } = string.Empty;
}
