FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["SwiftMere.Booking.Api.csproj", "./"]
RUN dotnet restore "SwiftMere.Booking.Api.csproj"

COPY . .
RUN dotnet publish "SwiftMere.Booking.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SwiftMere.Booking.Api.dll"]
