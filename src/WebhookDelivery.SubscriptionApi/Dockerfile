# ============================================================================
# Dockerfile for WebhookDelivery.SubscriptionApi (Web API)
# ============================================================================

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/WebhookDelivery.SubscriptionApi/WebhookDelivery.SubscriptionApi.csproj", "src/WebhookDelivery.SubscriptionApi/"]
COPY ["src/WebhookDelivery.Core/WebhookDelivery.Core.csproj", "src/WebhookDelivery.Core/"]
RUN dotnet restore "src/WebhookDelivery.SubscriptionApi/WebhookDelivery.SubscriptionApi.csproj"

COPY . .
WORKDIR "/src/src/WebhookDelivery.SubscriptionApi"
RUN dotnet build "WebhookDelivery.SubscriptionApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WebhookDelivery.SubscriptionApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN groupadd -r appuser && useradd -r -g appuser appuser
USER appuser

COPY --from=publish /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5001

EXPOSE 5001

ENTRYPOINT ["dotnet", "WebhookDelivery.SubscriptionApi.dll"]
