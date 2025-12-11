using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Xunit;

namespace WebhookDelivery.IntegrationTests;

/// <summary>
/// Base class for integration tests with database setup/teardown
/// </summary>
public abstract class TestBase : IAsyncLifetime
{
    protected string ConnectionString { get; private set; } = null!;
    protected string DatabaseName { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        // Load configuration
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Test.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Use test database
        DatabaseName = $"webhook_delivery_test_{Guid.NewGuid():N}";
        var baseConnectionString = config.GetConnectionString("TestDatabase")
            ?? "Server=localhost;Port=3306;Uid=root;Pwd=test;CharSet=utf8mb4;";

        // Create test database
        await using var connection = new MySqlConnection(baseConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE `{DatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
        await cmd.ExecuteNonQueryAsync();

        // Update connection string to use test database
        ConnectionString = $"{baseConnectionString}Database={DatabaseName};";

        // Run schema migration
        await RunSchemaMigrationAsync();
    }

    public virtual async Task DisposeAsync()
    {
        // Drop test database
        if (!string.IsNullOrEmpty(DatabaseName))
        {
            try
            {
                var baseConnectionString = ConnectionString.Split("Database=")[0];
                await using var connection = new MySqlConnection(baseConnectionString);
                await connection.OpenAsync();
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = $"DROP DATABASE IF EXISTS `{DatabaseName}`;";
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private async Task RunSchemaMigrationAsync()
    {
        // Read and execute schema migration
        var schemaPath = "../../../src/WebhookDelivery.Database/Migrations/001_InitialSchema.sql";
        if (System.IO.File.Exists(schemaPath))
        {
            var schemaSql = await System.IO.File.ReadAllTextAsync(schemaPath);

            await using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            // Split by delimiter and execute each statement
            var statements = schemaSql.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var statement in statements)
            {
                var trimmed = statement.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("--"))
                    continue;

                await using var cmd = connection.CreateCommand();
                cmd.CommandText = trimmed + ";";
                try
                {
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    // Log but continue for comments/non-critical statements
                    Console.WriteLine($"Schema execution warning: {ex.Message}");
                }
            }
        }
    }

    protected async Task<long> InsertTestEventAsync(string eventType = "test.event", string payload = "{\"test\":true}")
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO events (event_type, payload, created_at)
            VALUES (@EventType, @Payload, UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
        ";
        cmd.Parameters.AddWithValue("@EventType", eventType);
        cmd.Parameters.AddWithValue("@Payload", payload);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    protected async Task<long> InsertTestSubscriptionAsync(
        string eventType = "test.event",
        string callbackUrl = "https://example.com/webhook",
        bool active = true,
        bool verified = true)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO subscriptions (event_type, callback_url, active, verified, created_at, updated_at)
            VALUES (@EventType, @CallbackUrl, @Active, @Verified, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
        ";
        cmd.Parameters.AddWithValue("@EventType", eventType);
        cmd.Parameters.AddWithValue("@CallbackUrl", callbackUrl);
        cmd.Parameters.AddWithValue("@Active", active);
        cmd.Parameters.AddWithValue("@Verified", verified);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    protected async Task<long> InsertTestSagaAsync(
        long eventId,
        long subscriptionId,
        string status = "Pending",
        int attemptCount = 0)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO webhook_delivery_sagas
                (event_id, subscription_id, status, attempt_count, next_attempt_at, created_at, updated_at)
            VALUES
                (@EventId, @SubscriptionId, @Status, @AttemptCount, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
        ";
        cmd.Parameters.AddWithValue("@EventId", eventId);
        cmd.Parameters.AddWithValue("@SubscriptionId", subscriptionId);
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@AttemptCount", attemptCount);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    protected async Task<long> InsertTestJobAsync(
        long sagaId,
        string status = "Pending")
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO webhook_delivery_jobs
                (saga_id, status, attempt_at)
            VALUES
                (@SagaId, @Status, UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
        ";
        cmd.Parameters.AddWithValue("@SagaId", sagaId);
        cmd.Parameters.AddWithValue("@Status", status);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
