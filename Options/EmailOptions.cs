namespace SwiftMere.Booking.Api.Options;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string ResendApiKey { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string AdminTo { get; init; } = string.Empty;
}
