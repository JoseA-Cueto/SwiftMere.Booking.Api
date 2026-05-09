namespace SwiftMere.Booking.Api.Options;

public sealed class GoogleCalendarOptions
{
    public const string SectionName = "GoogleCalendar";

    public string AuthMode { get; init; } = "ServiceAccount";
    public string ServiceAccountEmail { get; init; } = string.Empty;
    public string PrivateKey { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public string CalendarId { get; init; } = string.Empty;
}
