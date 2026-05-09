namespace SwiftMere.Booking.Api.Services;

public interface IBookingEmailSender
{
    Task SendBookingConfirmationAsync(
        BookingEmailContext context,
        CancellationToken cancellationToken);
}
