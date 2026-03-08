using Azure.Messaging.ServiceBus;
using System.Text.Json;
using EmergencyPlatform.Models;

namespace EmergencyPlatform.Services;

/// <summary>
/// Publishes classified incidents to Azure Service Bus for downstream consumers
/// (dispatch systems, dashboards, logging pipelines).
/// Config: ServiceBus:ConnectionString, ServiceBus:QueueName
/// </summary>
public class ServiceBusPublisher : IAsyncDisposable
{
    private readonly ServiceBusClient   _client;
    private readonly ServiceBusSender   _sender;
    private readonly ILogger<ServiceBusPublisher> _logger;

    public ServiceBusPublisher(IConfiguration config,
        ILogger<ServiceBusPublisher> logger)
    {
        _logger = logger;
        var cs    = config["ServiceBus:ConnectionString"]!;
        var queue = config["ServiceBus:QueueName"] ?? "incidents";
        _client = new ServiceBusClient(cs);
        _sender = _client.CreateSender(queue);
    }

    public async Task PublishAsync(IncidentReport report)
    {
        try
        {
            var payload = JsonSerializer.Serialize(report);
            var msg = new ServiceBusMessage(payload)
            {
                Subject       = $"INCIDENT:{report.IncidentType.ToUpper()}",
                ContentType   = "application/json",
                CorrelationId = report.IncidentId
            };
            msg.ApplicationProperties["severity"] = report.SeverityLevel;
            msg.ApplicationProperties["priority"] = report.DispatchRecommendation.Priority;

            await _sender.SendMessageAsync(msg);
            _logger.LogInformation("Incident {Id} published to Service Bus", report.IncidentId);
        }
        catch (Exception ex)
        {
            // Don't let messaging failure block the API response
            _logger.LogError(ex, "Failed to publish incident {Id}", report.IncidentId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
    }
}
