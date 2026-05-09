using SwiftMere.Booking.Api.Contracts;

namespace SwiftMere.Booking.Api.Services;

public interface IBookingService
{
    Task<AvailabilityResponse> GetAvailabilityAsync(
        DateOnly? from,
        int? days,
        CancellationToken cancellationToken);

    Task<BookingResponse> CreateBookingAsync(
        CreateBookingRequest request,
        CancellationToken cancellationToken);
}
