namespace SwiftMere.Booking.Api.Contracts;

public sealed record AvailabilityResponse(
    string TimeZone,
    int DurationMinutes,
    IReadOnlyList<AvailableSlotResponse> Slots);

public sealed record AvailableSlotResponse(
    DateTimeOffset Start,
    DateTimeOffset End);
