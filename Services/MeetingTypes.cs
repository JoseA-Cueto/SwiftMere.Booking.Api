namespace SwiftMere.Booking.Api.Services;

public static class MeetingTypes
{
    public const string GoogleMeet = "google_meet";
    public const string Phone = "phone";
    public const string Teams = "teams";

    public static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            Phone => Phone,
            Teams => Teams,
            _ => GoogleMeet,
        };
    }

    public static string Label(string meetingType, string lang)
    {
        var isEnglish = lang == "en";

        return meetingType switch
        {
            Phone => isEnglish ? "Phone call" : "Llamada telefonica",
            Teams => "Microsoft Teams",
            _ => "Google Meet",
        };
    }
}
