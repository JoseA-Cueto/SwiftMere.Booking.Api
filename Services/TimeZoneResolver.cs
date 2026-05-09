using System.Runtime.InteropServices;

namespace SwiftMere.Booking.Api.Services;

public static class TimeZoneResolver
{
    private static readonly IReadOnlyDictionary<string, string> IanaToWindows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Europe/Madrid"] = "Romance Standard Time",
        ["Europe/Paris"] = "Romance Standard Time",
        ["UTC"] = "UTC",
    };

    public static TimeZoneInfo Find(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException) when (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && IanaToWindows.TryGetValue(timeZoneId, out var windowsId))
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
    }
}
