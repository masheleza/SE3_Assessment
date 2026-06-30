using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using FinancialStatements.BFF.Services;
using FinancialStatements.Models.Events;

namespace FinancialStatements.BFF.Workers;

public sealed class DocumentReadyConsumer : BackgroundService
{
    private readonly IAmazonSQS _sqs;
    private readonly IServiceProvider _services;
    private readonly ILogger<DocumentReadyConsumer> _logger;
    private readonly string _responseQueueUrl;

    public DocumentReadyConsumer(
        IAmazonSQS sqs,
        IServiceProvider services,
        IConfiguration config,
        ILogger<DocumentReadyConsumer> logger)
    {
        _sqs = sqs;
        _services = services;
        _logger = logger;
        _responseQueueUrl = config["Sqs:ResponseQueueUrl"]
            ?? throw new InvalidOperationException("Sqs:ResponseQueueUrl is not configured.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DocumentReadyConsumer started, polling {Queue}", _responseQueueUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in DocumentReadyConsumer, retrying in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var request = new ReceiveMessageRequest
        {
            QueueUrl = _responseQueueUrl,
            MaxNumberOfMessages = 10,
            WaitTimeSeconds = 20,
            MessageAttributeNames = ["MessageType"]
        };

        var response = await _sqs.ReceiveMessageAsync(request, ct);

        foreach (var message in response.Messages)
        {
            await ProcessMessageAsync(message, ct);
        }
    }

    private async Task ProcessMessageAsync(Message sqsMessage, CancellationToken ct)
    {
        try
        {
            var @event = JsonSerializer.Deserialize<DocumentReadyEvent>(sqsMessage.Body);
            if (@event is null) return;

            await using var scope = _services.CreateAsyncScope();
            var notification = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var secureLinkService = scope.ServiceProvider.GetRequiredService<ISecureLinkService>();

            if (@event.Success)
            {
                var link = await secureLinkService.GenerateAsync(@event.DocumentId, @event.UserId, ct);
                await notification.NotifyDocumentReadyAsync(@event.UserId, @event.DocumentId, link, ct);
            }
            else
            {
                await notification.NotifyErrorAsync(@event.UserId, @event.DocumentId, @event.ErrorMessage ?? "Unknown error", ct);
            }

            await _sqs.DeleteMessageAsync(_responseQueueUrl, sqsMessage.ReceiptHandle, ct);
            _logger.LogInformation("Processed DocumentReadyEvent for DocumentId={DocumentId}", @event.DocumentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process SQS message {MessageId}", sqsMessage.MessageId);
        }
    }
}
