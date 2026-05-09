using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SwiftMere.Booking.Api.Contracts;
using SwiftMere.Booking.Api.Options;

namespace SwiftMere.Booking.Api.Services;

public sealed partial class BookingService(
    ICalendarClient calendarClient,
    IBookingEmailSender emailSender,
    IOptions<BookingOptions> optionsAccessor,
    ILogger<BookingService> logger) : IBookingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SlotLocks = new();
    private readonly BookingOptions _options = optionsAccessor.Value;

    public async Task<AvailabilityResponse> GetAvailabilityAsync(
        DateOnly? from,
        int? days,
        CancellationToken cancellationToken)
    {
        var rules = GetRules();
        var candidates = GenerateCandidateSlots(rules, from, days);

        if (candidates.Count == 0)
        {
            return new AvailabilityResponse(_options.TimeZone, _options.DurationMinutes, []);
        }

        var busy = await calendarClient.GetBusyRangesAsync(
            candidates[0].Start,
            candidates[^1].End,
            cancellationToken);

        var earliestAllowed = DateTimeOffset.UtcNow.AddMinutes(_options.MinNoticeMinutes);
        var slots = candidates
            .Where(slot => slot.Start >= earliestAllowed)
            .Where(slot => !OverlapsBusy(slot.Start, slot.End, busy))
            .Select(slot => new AvailableSlotResponse(slot.Start, slot.End))
            .ToArray();

        return new AvailabilityResponse(_options.TimeZone, _options.DurationMinutes, slots);
    }

    public async Task<BookingResponse> CreateBookingAsync(
        CreateBookingRequest request,
        CancellationToken cancellationToken)
    {
        var rules = GetRules();
        var normalized = NormalizeRequest(request);
        var start = normalized.Start.ToUniversalTime();
        var end = start.AddMinutes(_options.DurationMinutes);

        ValidateSlot(rules, start, end);

        var lockKey = start.UtcTicks.ToString(CultureInfo.InvariantCulture);
        var slotLock = SlotLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await slotLock.WaitAsync(cancellationToken);
        try
        {
            var busy = await calendarClient.GetBusyRangesAsync(start, end, cancellationToken);
            if (OverlapsBusy(start, end, busy))
            {
                throw new ApiException(
                    StatusCodes.Status409Conflict,
                    "This slot was just booked. Please choose another time.");
            }

            var draft = new CalendarEventDraft(
                start,
                end,
                normalized.Name,
                normalized.Email,
                normalized.Company,
                normalized.Phone,
                normalized.Notes,
                normalized.MeetingType,
                normalized.Lang);

            var calendarEvent = await calendarClient.CreateEventAsync(draft, cancellationToken);
            var formattedWhen = FormatWhen(start, rules.TimeZoneInfo, normalized.Lang);

            await emailSender.SendBookingConfirmationAsync(
                new BookingEmailContext(draft, calendarEvent, _options.TimeZone, formattedWhen),
                cancellationToken);

            return new BookingResponse(
                start,
                end,
                _options.TimeZone,
                formattedWhen,
                normalized.MeetingType,
                calendarEvent.MeetingUrl,
                calendarEvent.Id,
                calendarEvent.HtmlLink);
        }
        finally
        {
            slotLock.Release();
        }
    }

    private IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> GenerateCandidateSlots(
        BookingRules rules,
        DateOnly? from,
        int? requestedDays)
    {
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, rules.TimeZoneInfo);
        var startDate = from ?? DateOnly.FromDateTime(nowLocal.DateTime);
        var days = Math.Clamp(requestedDays ?? _options.LookaheadDays, 1, 45);
        var slots = new List<(DateTimeOffset Start, DateTimeOffset End)>();

        for (var dayOffset = 0; dayOffset < days; dayOffset++)
        {
            var date = startDate.AddDays(dayOffset);
            if (!_options.WorkDays.Contains((int)date.DayOfWeek))
            {
                continue;
            }

            for (var time = rules.WorkdayStart;
                 time.Add(TimeSpan.FromMinutes(_options.DurationMinutes)) <= rules.WorkdayEnd;
                 time = time.Add(TimeSpan.FromMinutes(_options.IntervalMinutes)))
            {
                var localStart = date.ToDateTime(TimeOnly.FromTimeSpan(time), DateTimeKind.Unspecified);

                if (rules.TimeZoneInfo.IsInvalidTime(localStart))
                {
                    continue;
                }

                var utcStart = TimeZoneInfo.ConvertTimeToUtc(localStart, rules.TimeZoneInfo);
                var start = new DateTimeOffset(utcStart, TimeSpan.Zero);
                var end = start.AddMinutes(_options.DurationMinutes);
                slots.Add((start, end));
            }
        }

        return slots;
    }

    private void ValidateSlot(BookingRules rules, DateTimeOffset start, DateTimeOffset end)
    {
        if (start < DateTimeOffset.UtcNow.AddMinutes(_options.MinNoticeMinutes))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "This slot is no longer available.");
        }

        var localStart = TimeZoneInfo.ConvertTime(start, rules.TimeZoneInfo);
        var localEnd = TimeZoneInfo.ConvertTime(end, rules.TimeZoneInfo);
        var startTime = localStart.TimeOfDay;
        var windowStart = localStart.Date.Add(rules.WorkdayStart);
        var windowEnd = localStart.Date.Add(rules.WorkdayEnd);

        if (!_options.WorkDays.Contains((int)localStart.DayOfWeek) ||
            localStart.DateTime < windowStart ||
            localEnd.DateTime > windowEnd)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "This slot is outside booking hours.");
        }

        var minutesFromStart = (startTime - rules.WorkdayStart).TotalMinutes;
        if (minutesFromStart < 0 || minutesFromStart % _options.IntervalMinutes != 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "This slot is not a valid booking interval.");
        }
    }

    private NormalizedBookingRequest NormalizeRequest(CreateBookingRequest request)
    {
        var name = Clean(request.Name, 120);
        var email = Clean(request.Email, 160).ToLowerInvariant();
        var company = CleanOptional(request.Company, 160);
        var phone = CleanOptional(request.Phone, 80);
        var notes = CleanOptional(request.Notes, 800);
        var meetingType = MeetingTypes.Normalize(request.MeetingType);
        var lang = request.Lang == "en" ? "en" : "es";

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "Name is required.");
        }

        if (!EmailRegex().IsMatch(email))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "A valid email is required.");
        }

        if (meetingType == MeetingTypes.Phone && string.IsNullOrWhiteSpace(phone))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "Phone number is required for phone calls.");
        }

        return new NormalizedBookingRequest(
            request.Start,
            name,
            email,
            company,
            phone,
            notes,
            meetingType,
            lang);
    }

    private BookingRules GetRules()
    {
        if (_options.DurationMinutes <= 0 ||
            _options.IntervalMinutes <= 0 ||
            _options.LookaheadDays <= 0 ||
            _options.MinNoticeMinutes < 0)
        {
            throw new ApiException(StatusCodes.Status500InternalServerError, "Booking configuration is invalid.");
        }

        if (!TryParseBookingTime(_options.WorkdayStart, allowEndOfDay: false, out var workdayStart) ||
            !TryParseBookingTime(_options.WorkdayEnd, allowEndOfDay: true, out var workdayEnd) ||
            workdayStart >= workdayEnd)
        {
            throw new ApiException(StatusCodes.Status500InternalServerError, "Booking hours configuration is invalid.");
        }

        try
        {
            return new BookingRules(TimeZoneResolver.Find(_options.TimeZone), workdayStart, workdayEnd);
        }
        catch (TimeZoneNotFoundException exception)
        {
            logger.LogError(exception, "Configured booking time zone was not found: {TimeZone}", _options.TimeZone);
            throw new ApiException(StatusCodes.Status500InternalServerError, "Booking time zone is invalid.");
        }
    }

    private static bool OverlapsBusy(
        DateTimeOffset slotStart,
        DateTimeOffset slotEnd,
        IReadOnlyList<BusyRange> busyRanges)
    {
        return busyRanges.Any(busy => slotStart < busy.End && slotEnd > busy.Start);
    }

    private static string FormatWhen(DateTimeOffset start, TimeZoneInfo timeZoneInfo, string lang)
    {
        var culture = CultureInfo.GetCultureInfo(lang == "en" ? "en-US" : "es-ES");
        var localStart = TimeZoneInfo.ConvertTime(start, timeZoneInfo);
        return localStart.ToString("dddd, d MMMM yyyy HH:mm", culture);
    }

    private static bool TryParseBookingTime(string value, bool allowEndOfDay, out TimeSpan time)
    {
        if (allowEndOfDay &&
            value.Trim().Equals("24:00", StringComparison.Ordinal))
        {
            time = TimeSpan.FromDays(1);
            return true;
        }

        return TimeSpan.TryParseExact(value, "hh\\:mm", CultureInfo.InvariantCulture, out time);
    }

    private static string Clean(string value, int maxLength)
    {
        return WhitespaceRegex()
            .Replace(value.Trim(), " ")
            .Truncate(maxLength);
    }

    private static string? CleanOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Clean(value, maxLength);
    }

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    private sealed record BookingRules(
        TimeZoneInfo TimeZoneInfo,
        TimeSpan WorkdayStart,
        TimeSpan WorkdayEnd);

    private sealed record NormalizedBookingRequest(
        DateTimeOffset Start,
        string Name,
        string Email,
        string? Company,
        string? Phone,
        string? Notes,
        string MeetingType,
        string Lang);
}

file static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
