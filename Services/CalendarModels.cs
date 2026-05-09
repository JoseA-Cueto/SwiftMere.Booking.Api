namespace SwiftMere.Booking.Api.Services;

public sealed record BusyRange(DateTimeOffset Start, DateTimeOffset End);

public sealed record CalendarBookingEvent(
    string Id,
    string? HtmlLink,
    string? MeetingUrl);

public sealed record CalendarEventDraft(
    DateTimeOffset Start,
    DateTimeOffset End,
    string Name,
    string Email,
    string? Company,
    string? Phone,
    string? Notes,
    string MeetingType,
    string Lang);
