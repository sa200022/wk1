# ============================================================================
# Dockerfile for WebhookDelivery.EventIngestion (BackgroundService)
# ============================================================================

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["src/WebhookDelivery.EventIngestion/WebhookDelivery.EventIngestion.csproj", "src/WebhookDelivery.EventIngestion/"]
COPY ["src/WebhookDelivery.Core/WebhookDelivery.Core.csproj", "src/WebhookDelivery.Core/"]
RUN dotnet restore "src/WebhookDelivery.EventIngestion/WebhookDelivery.EventIngestion.csproj"

# Copy source code and build
COPY . .
WORKDIR "/src/src/WebhookDelivery.EventIngestion"
RUN dotnet build "WebhookDelivery.EventIngestion.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "WebhookDelivery.EventIngestion.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Create non-root user for security
RUN groupadd -r appuser && useradd -r -g appuser appuser
USER appuser

COPY --from=publish /app/publish .

# Environment variables (can be overridden by docker-compose)
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "WebhookDelivery.EventIngestion.dll"]
