using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using WebhookDelivery.Core.Repositories;
using WebhookDelivery.EventIngestion.Infrastructure;
using WebhookDelivery.EventIngestion.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Register PostgreSQL connection factory
builder.Services.AddScoped(_ =>
{
    var connectionString = builder.Configuration.GetSection("Database:ConnectionString").Value
        ?? throw new InvalidOperationException("Database connection string is not configured");
    return new NpgsqlConnection(connectionString);
});

// Register repositories
builder.Services.AddScoped<IEventRepository, PostgresEventRepository>();

// Register services
builder.Services.AddHostedService<EventIngestionService>();

var host = builder.Build();

await host.RunAsync();
