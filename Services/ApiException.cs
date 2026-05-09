namespace SwiftMere.Booking.Api.Services;

public sealed class ApiException : Exception
{
    public ApiException(int statusCode, string publicMessage, string? detail = null)
        : base(detail ?? publicMessage)
    {
        StatusCode = statusCode;
        PublicMessage = publicMessage;
    }

    public int StatusCode { get; }
    public string PublicMessage { get; }
}
