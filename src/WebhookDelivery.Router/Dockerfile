# ============================================================================
# Dockerfile for WebhookDelivery.Router (BackgroundService)
# ============================================================================

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/WebhookDelivery.Router/WebhookDelivery.Router.csproj", "src/WebhookDelivery.Router/"]
COPY ["src/WebhookDelivery.Core/WebhookDelivery.Core.csproj", "src/WebhookDelivery.Core/"]
RUN dotnet restore "src/WebhookDelivery.Router/WebhookDelivery.Router.csproj"

COPY . .
WORKDIR "/src/src/WebhookDelivery.Router"
RUN dotnet build "WebhookDelivery.Router.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WebhookDelivery.Router.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd -r appuser && useradd -r -g appuser appuser
USER appuser

COPY --from=publish /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "WebhookDelivery.Router.dll"]
