using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using WebhookDelivery.Core.Repositories;
using WebhookDelivery.SubscriptionApi.Controllers;
using WebhookDelivery.SubscriptionApi.Infrastructure;
using WebhookDelivery.SubscriptionApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register PostgreSQL connection factory
builder.Services.AddScoped(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection string is not configured");
    return new NpgsqlConnection(connectionString);
});

// Register repositories
builder.Services.AddScoped<ISubscriptionRepository, PostgresSubscriptionRepository>();

// Register services
builder.Services.AddScoped<SubscriptionService>();

var app = builder.Build();

// Swagger: always enable to便於驗證/對接 (若需關閉可改回條件式)
app.UseSwagger();
app.UseSwaggerUI();

var apiKey = app.Configuration["Security:ApiKey"];
if (!string.IsNullOrWhiteSpace(apiKey))
{
    app.Use(async (context, next) =>
    {
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

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
