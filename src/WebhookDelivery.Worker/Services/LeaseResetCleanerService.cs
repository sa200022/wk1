using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace WebhookDelivery.Worker.Services;

/// <summary>
/// Lease Reset Cleaner - resets expired leased jobs back to Pending
/// Responsibilities:
/// - Periodically select Leased jobs where lease_until < now
/// - Reset them to Pending (idempotent)
///
/// MUST NOT:
/// - Touch saga state
/// - Create jobs
/// - Delete jobs
/// </summary>
public sealed class LeaseResetCleanerService : BackgroundService
{
    private readonly string _connectionString;
    private readonly ILogger<LeaseResetCleanerService> _logger;
    private readonly TimeSpan _cleanupInterval;

    public LeaseResetCleanerService(
        IConfiguration configuration,
        ILogger<LeaseResetCleanerService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Database connection string is not configured");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cleanupInterval = TimeSpan.FromSeconds(configuration.GetValue<int>("LeaseCleaner:PollingIntervalSeconds", 30));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Lease Reset Cleaner started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ResetExpiredLeasesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Lease Reset Cleaner");
            }

            await Task.Delay(_cleanupInterval, stoppingToken);
        }

        _logger.LogInformation("Lease Reset Cleaner stopped");
    }

    private async Task ResetExpiredLeasesAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE webhook_delivery_jobs
            SET status = 'Pending',
                lease_until = NULL
            WHERE status = 'Leased'
              AND lease_until < NOW()
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var rowsAffected = await connection.ExecuteAsync(
            new CommandDefinition(sql, cancellationToken: cancellationToken)
        );

        if (rowsAffected > 0)
        {
            _logger.LogInformation(
                "Reset {Count} expired leased jobs to Pending",
                rowsAffected);
        }
    }
}
