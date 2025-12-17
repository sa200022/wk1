using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using WebhookDelivery.Core.Repositories;
using WebhookDelivery.EventIngestion.Infrastructure;
using WebhookDelivery.EventIngestion.Services;
using System.Text.Json;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddJsonConsole();
builder.Logging.AddDebug();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Database connection string is not configured");

// Register PostgreSQL connection factory
builder.Services.AddScoped(_ => new NpgsqlConnection(connectionString));

// Register repositories
builder.Services.AddScoped<IEventRepository>(_ => new PostgresEventRepository(connectionString));

// Register services
builder.Services.AddScoped<EventIngestionService>();
builder.Services.AddHostedService<EventIngestionWorker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var apiKey = app.Configuration["Security:ApiKey"];
if (!string.IsNullOrWhiteSpace(apiKey))
{
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var provided) ||
            provided != apiKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        await next();
    });
}

app.UseHttpMetrics();
app.MapMetrics("/metrics");

app.MapPost("/api/events", async (
    IngestEventRequest request,
    EventIngestionService ingestionService,
    CancellationToken cancellationToken) =>
{
    var payload = JsonDocument.Parse(request.Payload.GetRawText());
    var created = await ingestionService.IngestAsync(
        request.EventType,
        payload,
        request.ExternalEventId,
        cancellationToken);

    return Results.Created($"/api/events/{created.Id}", new
    {
        created.Id,
        created.EventType,
        created.ExternalEventId,
        created.CreatedAt
    });
});

app.MapGet("/health", () => Results.Ok("ok"));

await app.RunAsync();

public record IngestEventRequest(string EventType, JsonElement Payload, string? ExternalEventId);
