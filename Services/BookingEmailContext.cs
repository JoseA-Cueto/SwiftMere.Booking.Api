namespace SwiftMere.Booking.Api.Services;

public sealed record BookingEmailContext(
    CalendarEventDraft Draft,
    CalendarBookingEvent CalendarEvent,
    string TimeZone,
    string FormattedWhen);
