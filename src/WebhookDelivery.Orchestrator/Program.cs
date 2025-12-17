using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using WebhookDelivery.Core.Repositories;
using WebhookDelivery.Orchestrator.Infrastructure;
using WebhookDelivery.Orchestrator.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddJsonConsole();
builder.Logging.AddDebug();

// Register PostgreSQL connection factory
builder.Services.AddScoped(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection string is not configured");
    return new NpgsqlConnection(connectionString);
});

// Register repositories
builder.Services.AddScoped<ISagaRepository, PostgresSagaRepository>();
builder.Services.AddScoped<IJobRepository, PostgresJobRepository>();
builder.Services.AddScoped<IEventRepository, PostgresEventRepository>();
builder.Services.AddScoped<IDeadLetterRepository, PostgresDeadLetterRepository>();
builder.Services.AddHostedService(provider =>
{
    var logger = provider.GetRequiredService<ILogger<HealthServer>>();
    var port = builder.Configuration.GetValue<int>("Health:Port", 6002);
    return new HealthServer(logger, port);
});

// Register services
builder.Services.AddHostedService<SagaOrchestratorService>();

var host = builder.Build();

await host.RunAsync();
