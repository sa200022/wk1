# ============================================================================
# Dockerfile for WebhookDelivery.Worker (BackgroundService - Dual Services)
# ============================================================================

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/WebhookDelivery.Worker/WebhookDelivery.Worker.csproj", "src/WebhookDelivery.Worker/"]
COPY ["src/WebhookDelivery.Core/WebhookDelivery.Core.csproj", "src/WebhookDelivery.Core/"]
RUN dotnet restore "src/WebhookDelivery.Worker/WebhookDelivery.Worker.csproj"

COPY . .
WORKDIR "/src/src/WebhookDelivery.Worker"
RUN dotnet build "WebhookDelivery.Worker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WebhookDelivery.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN groupadd -r appuser && useradd -r -g appuser appuser
USER appuser

COPY --from=publish /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "WebhookDelivery.Worker.dll"]
