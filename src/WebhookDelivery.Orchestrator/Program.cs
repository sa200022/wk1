using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using WebhookDelivery.Core.Repositories;
using WebhookDelivery.Orchestrator.Infrastructure;
using WebhookDelivery.Orchestrator.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Register MySQL connection factory
builder.Services.AddScoped(_ =>
{
    var connectionString = builder.Configuration.GetSection("Database:ConnectionString").Value
        ?? throw new InvalidOperationException("Database connection string is not configured");
    return new MySqlConnection(connectionString);
});

// Register repositories
builder.Services.AddScoped<ISagaRepository, MySqlSagaRepository>();
builder.Services.AddScoped<IJobRepository, MySqlJobRepository>();
builder.Services.AddScoped<IEventRepository, MySqlEventRepository>();
builder.Services.AddScoped<IDeadLetterRepository, MySqlDeadLetterRepository>();

// Register services
builder.Services.AddHostedService<SagaOrchestratorService>();

var host = builder.Build();

await host.RunAsync();
