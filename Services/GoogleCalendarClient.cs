using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SwiftMere.Booking.Api.Options;

namespace SwiftMere.Booking.Api.Services;

public sealed class GoogleCalendarClient(
    HttpClient httpClient,
    IOptions<GoogleCalendarOptions> calendarOptions,
    IOptions<BookingOptions> bookingOptions,
    ILogger<GoogleCalendarClient> logger) : ICalendarClient
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string CalendarApiBase = "https://www.googleapis.com/calendar/v3";
    private const string CalendarScope = "https://www.googleapis.com/auth/calendar";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly GoogleCalendarOptions _calendarOptions = calendarOptions.Value;
    private readonly BookingOptions _bookingOptions = bookingOptions.Value;
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public async Task<IReadOnlyList<BusyRange>> GetBusyRangesAsync(
        DateTimeOffset timeMin,
        DateTimeOffset timeMax,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var payload = new
        {
            timeMin = timeMin.UtcDateTime.ToString("O"),
            timeMax = timeMax.UtcDateTime.ToString("O"),
            timeZone = _bookingOptions.TimeZone,
            items = new[] { new { id = _calendarOptions.CalendarId } },
        };

        var result = await SendGoogleAsync<FreeBusyResponse>(
            HttpMethod.Post,
            "/freeBusy",
            payload,
            cancellationToken);

        if (result.Calendars is null ||
            !result.Calendars.TryGetValue(_calendarOptions.CalendarId, out var calendar) ||
            calendar.Busy is null)
        {
            return [];
        }

        return calendar.Busy
            .Where(range => range.Start.HasValue && range.End.HasValue)
            .Select(range => new BusyRange(range.Start!.Value, range.End!.Value))
            .ToArray();
    }

    public async Task<CalendarBookingEvent> CreateEventAsync(
        CalendarEventDraft draft,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var createMeet = draft.MeetingType == MeetingTypes.GoogleMeet;
        var eventPayload = BuildEventPayload(draft, createMeet);
        var path = $"/calendars/{Uri.EscapeDataString(_calendarOptions.CalendarId)}/events?conferenceDataVersion=1";

        var created = await SendGoogleAsync<GoogleCalendarEventResponse>(
            HttpMethod.Post,
            path,
            eventPayload,
            cancellationToken);

        var meetingUrl = created.HangoutLink
            ?? created.ConferenceData?.EntryPoints?.FirstOrDefault(entry => entry.EntryPointType == "video")?.Uri;

        return new CalendarBookingEvent(created.Id ?? string.Empty, created.HtmlLink, meetingUrl);
    }

    private object BuildEventPayload(CalendarEventDraft draft, bool createMeet)
    {
        var description = string.Join("\n", new[]
        {
            "Swiftmere booking",
            "",
            $"Name: {draft.Name}",
            $"Email: {draft.Email}",
            string.IsNullOrWhiteSpace(draft.Company) ? null : $"Company: {draft.Company}",
            string.IsNullOrWhiteSpace(draft.Phone) ? null : $"Phone: {draft.Phone}",
            $"Meeting type: {MeetingTypes.Label(draft.MeetingType, "en")}",
            string.IsNullOrWhiteSpace(draft.Notes) ? null : "",
            string.IsNullOrWhiteSpace(draft.Notes) ? null : $"Notes: {draft.Notes}",
        }.Where(line => line is not null));

        return new
        {
            summary = $"Swiftmere discovery call - {draft.Name}",
            description,
            location = draft.MeetingType switch
            {
                MeetingTypes.Phone => draft.Phone,
                MeetingTypes.Teams => "Microsoft Teams - link pending",
                _ => null,
            },
            start = new
            {
                dateTime = draft.Start.UtcDateTime.ToString("O"),
                timeZone = _bookingOptions.TimeZone,
            },
            end = new
            {
                dateTime = draft.End.UtcDateTime.ToString("O"),
                timeZone = _bookingOptions.TimeZone,
            },
            guestsCanInviteOthers = false,
            reminders = new
            {
                useDefault = false,
                overrides = new[]
                {
                    new { method = "email", minutes = 24 * 60 },
                    new { method = "popup", minutes = 30 },
                },
            },
            conferenceData = createMeet
                ? new
                {
                    createRequest = new
                    {
                        requestId = Guid.NewGuid().ToString("N"),
                        conferenceSolutionKey = new { type = "hangoutsMeet" },
                    },
                }
                : null,
        };
    }

    private async Task<T> SendGoogleAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        var accessToken = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, $"{CalendarApiBase}{path}");

        request.Headers.Authorization = new("Bearer", accessToken);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Google Calendar request failed with status {StatusCode}: {Body}",
                response.StatusCode,
                responseBody);

            throw new ApiException(
                StatusCodes.Status502BadGateway,
                "Scheduling provider returned an error.",
                responseBody);
        }

        var result = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
        return result ?? throw new ApiException(
            StatusCodes.Status502BadGateway,
            "Scheduling provider returned an empty response.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) &&
            DateTimeOffset.UtcNow < _accessTokenExpiresAt.AddMinutes(-1))
        {
            return _accessToken;
        }

        EnsureConfigured();

        var now = DateTimeOffset.UtcNow;
        var header = new
        {
            alg = "RS256",
            typ = "JWT",
        };

        var claims = new
        {
            iss = _calendarOptions.ServiceAccountEmail,
            scope = CalendarScope,
            aud = TokenEndpoint,
            iat = now.ToUnixTimeSeconds(),
            exp = now.AddHours(1).ToUnixTimeSeconds(),
        };

        var unsignedToken = $"{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions))}.{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(claims, JsonOptions))}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(NormalizePrivateKey(_calendarOptions.PrivateKey));

        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(unsignedToken),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var token = $"{unsignedToken}.{Base64UrlEncode(signature)}";

        using var response = await httpClient.PostAsync(
            TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = token,
            }),
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Google token request failed with status {StatusCode}: {Body}",
                response.StatusCode,
                responseBody);

            throw new ApiException(
                StatusCodes.Status502BadGateway,
                "Could not authenticate scheduling provider.",
                responseBody);
        }

        var tokenResponse = JsonSerializer.Deserialize<GoogleTokenResponse>(responseBody, JsonOptions);
        if (string.IsNullOrWhiteSpace(tokenResponse?.AccessToken))
        {
            throw new ApiException(
                StatusCodes.Status502BadGateway,
                "Scheduling provider did not return an access token.");
        }

        _accessToken = tokenResponse.AccessToken;
        _accessTokenExpiresAt = now.AddSeconds(tokenResponse.ExpiresIn <= 0 ? 3600 : tokenResponse.ExpiresIn);

        return _accessToken;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_calendarOptions.ServiceAccountEmail) ||
            string.IsNullOrWhiteSpace(_calendarOptions.PrivateKey) ||
            string.IsNullOrWhiteSpace(_calendarOptions.CalendarId))
        {
            throw new ApiException(
                StatusCodes.Status503ServiceUnavailable,
                "Scheduling provider is not configured.");
        }
    }

    private static string NormalizePrivateKey(string value)
    {
        return value.Trim().Trim('"').Replace("\\n", "\n", StringComparison.Ordinal);
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal);
    }

    private sealed record GoogleTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record FreeBusyResponse(
        Dictionary<string, FreeBusyCalendar>? Calendars);

    private sealed record FreeBusyCalendar(
        IReadOnlyList<FreeBusyRange>? Busy);

    private sealed record FreeBusyRange(
        DateTimeOffset? Start,
        DateTimeOffset? End);

    private sealed record GoogleCalendarEventResponse(
        string? Id,
        string? HtmlLink,
        string? HangoutLink,
        ConferenceDataResponse? ConferenceData);

    private sealed record ConferenceDataResponse(
        IReadOnlyList<EntryPointResponse>? EntryPoints);

    private sealed record EntryPointResponse(
        string? EntryPointType,
        string? Uri);
}
