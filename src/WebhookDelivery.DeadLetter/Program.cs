using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WebhookDelivery.Core.Repositories;
using WebhookDelivery.DeadLetter.Infrastructure;
using WebhookDelivery.DeadLetter.Services;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddJsonConsole();
builder.Logging.AddDebug();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Database connection string is not configured");

// Register repositories
builder.Services.AddScoped<IDeadLetterRepository>(_ => new PostgresDeadLetterRepository(connectionString));
builder.Services.AddScoped<ISagaRepository>(_ => new PostgresSagaRepository(connectionString));
builder.Services.AddScoped<IEventRepository>(_ => new PostgresEventRepository(connectionString));

// Register services
builder.Services.AddScoped<DeadLetterService>();

var app = builder.Build();

// Configure the HTTP request pipeline
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

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok("ok"));

await app.RunAsync();
