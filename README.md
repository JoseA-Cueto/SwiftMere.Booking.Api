# SwiftMere Booking API

API propia para sustituir Calendly en la landing de SwiftMere.

## Stack

- .NET 8 Web API
- Google Calendar API para disponibilidad y eventos
- Google Meet generado desde el evento de Calendar
- Resend para correos de confirmacion y aviso interno

## Endpoints

```http
GET /health
GET /api/availability?days=14
POST /api/bookings
```

### POST /api/bookings

```json
{
  "start": "2026-05-12T10:00:00Z",
  "name": "Jose",
  "email": "jose@example.com",
  "company": "Acme",
  "phone": "+34 600 000 000",
  "notes": "Quiero hablar de un MVP",
  "meetingType": "google_meet",
  "lang": "es"
}
```

`meetingType` soportado:

- `google_meet`: crea evento con enlace de Google Meet.
- `phone`: crea evento sin Meet y exige telefono.
- `teams`: reservado para una integracion futura con Microsoft Graph.

## Configuracion

Los secretos no deben ir en `appsettings.json`. Usar variables de entorno, user-secrets o el panel del hosting.

```powershell
dotnet user-secrets init
dotnet user-secrets set "GoogleCalendar:ServiceAccountEmail" "service-account@project.iam.gserviceaccount.com"
dotnet user-secrets set "GoogleCalendar:PrivateKey" "-----BEGIN PRIVATE KEY-----\n...\n-----END PRIVATE KEY-----\n"
dotnet user-secrets set "GoogleCalendar:CalendarId" "calendar-id@group.calendar.google.com"
dotnet user-secrets set "Email:ResendApiKey" "re_..."
dotnet user-secrets set "Email:From" "contacto@swiftmere.com"
dotnet user-secrets set "Email:AdminTo" "hello@swiftmere.com"
```

El calendario debe estar compartido con el service account para que pueda leer disponibilidad y crear eventos.

## Desarrollo

```powershell
dotnet restore
dotnet run
```

Swagger queda disponible en desarrollo.

## Deploy en Fly.io

El proyecto incluye `Dockerfile` y `fly.toml`.

```powershell
fly launch --no-deploy
fly secrets set GoogleCalendar__ServiceAccountEmail="service-account@project.iam.gserviceaccount.com"
fly secrets set GoogleCalendar__PrivateKey="-----BEGIN PRIVATE KEY-----\n...\n-----END PRIVATE KEY-----\n"
fly secrets set GoogleCalendar__CalendarId="calendar-id@group.calendar.google.com"
fly secrets set Email__ResendApiKey="re_..."
fly secrets set Email__From="contacto@swiftmere.com"
fly secrets set Email__AdminTo="hello@swiftmere.com"
fly deploy
```

Cuando Fly entregue la URL `https://swiftmere-booking-api.fly.dev`, configurar la landing:

```env
VITE_BOOKING_API_URL=https://swiftmere-booking-api.fly.dev
```

Si se usa dominio propio, por ejemplo `https://api.swiftmere.com`, cambiar `VITE_BOOKING_API_URL` y `Cors:AllowedOrigins`.
