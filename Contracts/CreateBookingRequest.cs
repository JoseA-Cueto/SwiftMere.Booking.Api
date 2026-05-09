namespace SwiftMere.Booking.Api.Contracts;

public sealed record CreateBookingRequest(
    DateTimeOffset Start,
    string Name,
    string Email,
    string? Company,
    string? Phone,
    string? Notes,
    string? MeetingType,
    string? Lang);
