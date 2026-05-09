namespace SwiftMere.Booking.Api.Contracts;

public sealed record BookingResponse(
    DateTimeOffset Start,
    DateTimeOffset End,
    string TimeZone,
    string When,
    string MeetingType,
    string? MeetingUrl,
    string CalendarEventId,
    string? CalendarUrl);
