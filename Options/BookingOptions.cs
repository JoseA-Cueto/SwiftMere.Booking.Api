namespace SwiftMere.Booking.Api.Options;

public sealed class BookingOptions
{
    public const string SectionName = "Booking";

    public string TimeZone { get; init; } = "Europe/Madrid";
    public int DurationMinutes { get; init; } = 30;
    public int IntervalMinutes { get; init; } = 30;
    public int LookaheadDays { get; init; } = 14;
    public int MinNoticeMinutes { get; init; } = 120;
    public string WorkdayStart { get; init; } = "09:00";
    public string WorkdayEnd { get; init; } = "17:00";
    public int[] WorkDays { get; init; } = [1, 2, 3, 4, 5];
}
