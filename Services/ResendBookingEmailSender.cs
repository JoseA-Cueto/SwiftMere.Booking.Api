using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;
using SwiftMere.Booking.Api.Options;

namespace SwiftMere.Booking.Api.Services;

public sealed class ResendBookingEmailSender(
    HttpClient httpClient,
    IOptions<EmailOptions> emailOptions,
    ILogger<ResendBookingEmailSender> logger) : IBookingEmailSender
{
    private const string ResendEndpoint = "https://api.resend.com/emails";
    private readonly EmailOptions _options = emailOptions.Value;

    public async Task SendBookingConfirmationAsync(
        BookingEmailContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ResendApiKey) ||
            string.IsNullOrWhiteSpace(_options.From) ||
            string.IsNullOrWhiteSpace(_options.AdminTo))
        {
            logger.LogWarning("Booking email was skipped because email configuration is incomplete.");
            return;
        }

        var clientSubject = context.Draft.Lang == "en"
            ? "Your meeting with Swiftmere is confirmed"
            : "Tu reunion con Swiftmere esta confirmada";

        var adminSubject = $"Nueva reunion - {context.Draft.Name}";

        await Task.WhenAll(
            SendEmailAsync(
                to: context.Draft.Email,
                subject: clientSubject,
                html: BuildClientHtml(context),
                replyTo: _options.AdminTo,
                cancellationToken),
            SendEmailAsync(
                to: _options.AdminTo,
                subject: adminSubject,
                html: BuildAdminHtml(context),
                replyTo: context.Draft.Email,
                cancellationToken));
    }

    private async Task SendEmailAsync(
        string to,
        string subject,
        string html,
        string? replyTo,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ResendEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ResendApiKey);
        request.Content = JsonContent.Create(new
        {
            from = $"Swiftmere <{_options.From}>",
            to = new[] { to },
            subject,
            html,
            reply_to = replyTo,
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Resend email request failed with status {StatusCode}: {Body}",
                response.StatusCode,
                responseBody);
        }
    }

    private static string BuildClientHtml(BookingEmailContext context)
    {
        var isEnglish = context.Draft.Lang == "en";
        var meetingLabel = MeetingTypes.Label(context.Draft.MeetingType, context.Draft.Lang);
        var name = Encode(context.Draft.Name);
        var meetingLink = Encode(context.CalendarEvent.MeetingUrl ?? string.Empty);

        return $"""
            <div style="background:#07121f;padding:40px 20px;font-family:Inter,Arial,sans-serif;">
              <div style="max-width:560px;margin:auto;background:#0d1b2e;border-radius:14px;padding:32px;border:1px solid rgba(255,255,255,0.07);">
                <p style="margin:0 0 8px;color:#85B7EB;font-size:12px;text-transform:uppercase;letter-spacing:1.5px;">{(isEnglish ? "Meeting booked" : "Reunion agendada")}</p>
                <h2 style="color:#ffffff;margin:0 0 16px;">{(isEnglish ? "Your meeting with Swiftmere is confirmed" : "Tu reunion con Swiftmere esta confirmada")}</h2>
                <p style="color:rgba(255,255,255,0.75);line-height:1.7;">{(isEnglish ? $"Hi {name}," : $"Hola {name},")}</p>
                <p style="color:rgba(255,255,255,0.65);line-height:1.7;">{(isEnglish ? "Your slot is reserved. You should also receive a calendar invitation." : "Hemos reservado tu espacio. Tambien deberias recibir una invitacion de calendario.")}</p>
                <div style="margin:24px 0;padding:16px;background:rgba(55,138,221,0.08);border:1px solid rgba(55,138,221,0.2);border-radius:10px;">
                  <p style="margin:0 0 8px;color:#85B7EB;font-size:13px;">{Encode(context.FormattedWhen)}</p>
                  <p style="margin:0;color:rgba(255,255,255,0.65);font-size:13px;">{Encode(meetingLabel)} · {Encode(context.TimeZone)}</p>
                  {(string.IsNullOrWhiteSpace(context.CalendarEvent.MeetingUrl) ? "" : $"<p style=\"margin:16px 0 0;\"><a href=\"{meetingLink}\" style=\"color:#85B7EB;\">{(isEnglish ? "Open meeting link" : "Abrir enlace de reunion")}</a></p>")}
                </div>
                <p style="color:rgba(255,255,255,0.4);font-size:12px;">{(isEnglish ? "If you need to change the time, reply to this email." : "Si necesitas cambiar la hora, responde a este correo.")}</p>
                <p style="color:rgba(255,255,255,0.65);font-size:14px;margin-top:24px;">Swiftmere</p>
              </div>
            </div>
            """;
    }

    private static string BuildAdminHtml(BookingEmailContext context)
    {
        var draft = context.Draft;

        return $"""
            <div style="background:#07121f;padding:40px 20px;font-family:Inter,Arial,sans-serif;">
              <div style="max-width:560px;margin:auto;background:#0d1b2e;border-radius:14px;padding:32px;border:1px solid rgba(255,255,255,0.07);">
                <p style="margin:0 0 8px;color:#85B7EB;font-size:12px;text-transform:uppercase;letter-spacing:1.5px;">Nueva reunion</p>
                <h2 style="color:#ffffff;margin:0 0 20px;">{Encode(draft.Name)} agendo una llamada</h2>
                <div style="color:rgba(255,255,255,0.75);line-height:1.7;">
                  <p><strong>Fecha:</strong> {Encode(context.FormattedWhen)}</p>
                  <p><strong>Email:</strong> {Encode(draft.Email)}</p>
                  {(string.IsNullOrWhiteSpace(draft.Company) ? "" : $"<p><strong>Empresa:</strong> {Encode(draft.Company)}</p>")}
                  {(string.IsNullOrWhiteSpace(draft.Phone) ? "" : $"<p><strong>Telefono:</strong> {Encode(draft.Phone)}</p>")}
                  <p><strong>Tipo:</strong> {Encode(MeetingTypes.Label(draft.MeetingType, "es"))}</p>
                  {(string.IsNullOrWhiteSpace(context.CalendarEvent.MeetingUrl) ? "" : $"<p><strong>Meet:</strong> <a href=\"{Encode(context.CalendarEvent.MeetingUrl)}\" style=\"color:#85B7EB;\">{Encode(context.CalendarEvent.MeetingUrl)}</a></p>")}
                  {(string.IsNullOrWhiteSpace(context.CalendarEvent.HtmlLink) ? "" : $"<p><strong>Calendar:</strong> <a href=\"{Encode(context.CalendarEvent.HtmlLink)}\" style=\"color:#85B7EB;\">Abrir evento</a></p>")}
                </div>
                {(string.IsNullOrWhiteSpace(draft.Notes) ? "" : $"<div style=\"margin-top:20px;padding:16px;background:#1a2d45;border-radius:10px;color:rgba(255,255,255,0.75);line-height:1.6;\">{Encode(draft.Notes)}</div>")}
              </div>
            </div>
            """;
    }

    private static string Encode(string? value)
    {
        return HtmlEncoder.Default.Encode(value ?? string.Empty);
    }
}
