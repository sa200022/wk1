# ============================================================================
# Dockerfile for WebhookDelivery.DeadLetter (Web API)
# ============================================================================

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/WebhookDelivery.DeadLetter/WebhookDelivery.DeadLetter.csproj", "src/WebhookDelivery.DeadLetter/"]
COPY ["src/WebhookDelivery.Core/WebhookDelivery.Core.csproj", "src/WebhookDelivery.Core/"]
RUN dotnet restore "src/WebhookDelivery.DeadLetter/WebhookDelivery.DeadLetter.csproj"

COPY . .
WORKDIR "/src/src/WebhookDelivery.DeadLetter"
RUN dotnet build "WebhookDelivery.DeadLetter.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WebhookDelivery.DeadLetter.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN groupadd -r appuser && useradd -r -g appuser appuser
USER appuser

COPY --from=publish /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5003

EXPOSE 5003

ENTRYPOINT ["dotnet", "WebhookDelivery.DeadLetter.dll"]
