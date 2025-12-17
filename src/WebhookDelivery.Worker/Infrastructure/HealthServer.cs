using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WebhookDelivery.Worker.Infrastructure;

public sealed class HealthServer : BackgroundService
{
    private readonly ILogger<HealthServer> _logger;
    private readonly HttpListener _listener = new();
    private readonly int _port;

    public HealthServer(ILogger<HealthServer> logger, int port = 6003)
    {
        _logger = logger;
        _port = port;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener.Prefixes.Add($"http://localhost:{_port}/health/");
        _listener.Start();
        stoppingToken.Register(() => _listener.Stop());
        _logger.LogInformation("Worker health server listening on port {Port}", _port);

        while (!stoppingToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            var response = context.Response;
            response.StatusCode = (int)HttpStatusCode.OK;
            await response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("ok"));
            response.Close();
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _listener.Stop();
        return base.StopAsync(cancellationToken);
    }
}
