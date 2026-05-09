namespace SwiftMere.Booking.Api.Services;

public interface ICalendarClient
{
    Task<IReadOnlyList<BusyRange>> GetBusyRangesAsync(
        DateTimeOffset timeMin,
        DateTimeOffset timeMax,
        CancellationToken cancellationToken);

    Task<CalendarBookingEvent> CreateEventAsync(
        CalendarEventDraft draft,
        CancellationToken cancellationToken);
}
