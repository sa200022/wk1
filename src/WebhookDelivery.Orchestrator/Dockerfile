# ============================================================================
# Dockerfile for WebhookDelivery.Orchestrator (BackgroundService)
# ============================================================================

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/WebhookDelivery.Orchestrator/WebhookDelivery.Orchestrator.csproj", "src/WebhookDelivery.Orchestrator/"]
COPY ["src/WebhookDelivery.Core/WebhookDelivery.Core.csproj", "src/WebhookDelivery.Core/"]
RUN dotnet restore "src/WebhookDelivery.Orchestrator/WebhookDelivery.Orchestrator.csproj"

COPY . .
WORKDIR "/src/src/WebhookDelivery.Orchestrator"
RUN dotnet build "WebhookDelivery.Orchestrator.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WebhookDelivery.Orchestrator.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd -r appuser && useradd -r -g appuser appuser
USER appuser

COPY --from=publish /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "WebhookDelivery.Orchestrator.dll"]
