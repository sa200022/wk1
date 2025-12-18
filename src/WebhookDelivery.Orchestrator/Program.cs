using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebhookDelivery.Core.Repositories;
using WebhookDelivery.Orchestrator.Infrastructure;
using WebhookDelivery.Orchestrator.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddJsonConsole();
builder.Logging.AddDebug();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Database connection string is not configured");

// Register repositories
builder.Services.AddSingleton<ISagaRepository>(_ => new PostgresSagaRepository(connectionString));
builder.Services.AddSingleton<IJobRepository>(_ => new PostgresJobRepository(connectionString));
builder.Services.AddSingleton<IEventRepository>(_ => new PostgresEventRepository(connectionString));
builder.Services.AddSingleton<IDeadLetterRepository>(_ => new PostgresDeadLetterRepository(connectionString));
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
