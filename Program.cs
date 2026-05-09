using Microsoft.AspNetCore.HttpOverrides;
using SwiftMere.Booking.Api.Contracts;
using SwiftMere.Booking.Api.Options;
using SwiftMere.Booking.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BookingOptions>(
    builder.Configuration.GetSection(BookingOptions.SectionName));
builder.Services.Configure<GoogleCalendarOptions>(
    builder.Configuration.GetSection(GoogleCalendarOptions.SectionName));
builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection(EmailOptions.SectionName));

var corsOptions = builder.Configuration
    .GetSection(CorsOptions.SectionName)
    .Get<CorsOptions>() ?? new CorsOptions();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Landing", policy =>
    {
        if (corsOptions.AllowedOrigins.Length > 0)
        {
            policy.WithOrigins(corsOptions.AllowedOrigins);
        }
        else
        {
            policy.AllowAnyOrigin();
        }

        policy.AllowAnyHeader();
        policy.AllowAnyMethod();
    });
});

builder.Services.AddHttpClient<ICalendarClient, GoogleCalendarClient>();
builder.Services.AddHttpClient<IBookingEmailSender, ResendBookingEmailSender>();
builder.Services.AddScoped<IBookingService, BookingService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Landing");

app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (ApiException exception)
    {
        context.Response.StatusCode = exception.StatusCode;
        await context.Response.WriteAsJsonAsync(new ProblemResponse(exception.PublicMessage));
    }
    catch (Exception exception)
    {
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("UnhandledException");

        logger.LogError(exception, "Unhandled booking API exception.");

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new ProblemResponse("Unexpected server error."));
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health")
    .WithOpenApi();

var api = app.MapGroup("/api");

api.MapGet("/availability", async (
        DateOnly? from,
        int? days,
        IBookingService bookingService,
        CancellationToken cancellationToken) =>
    {
        var response = await bookingService.GetAvailabilityAsync(from, days, cancellationToken);
        return Results.Ok(response);
    })
    .WithName("GetAvailability")
    .WithOpenApi();

api.MapPost("/bookings", async (
        CreateBookingRequest request,
        IBookingService bookingService,
        CancellationToken cancellationToken) =>
    {
        var response = await bookingService.CreateBookingAsync(request, cancellationToken);
        return Results.Created($"/api/bookings/{response.CalendarEventId}", response);
    })
    .WithName("CreateBooking")
    .WithOpenApi();

app.Run();
