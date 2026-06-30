using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using FinancialStatements.DocumentApi.Services;
using FinancialStatements.Models.Events;

namespace FinancialStatements.DocumentApi.Consumers;

public sealed class SqsDocumentConsumer : BackgroundService
{
    private readonly IAmazonSQS _sqs;
    private readonly IServiceProvider _services;
    private readonly string _queueUrl;
    private readonly ILogger<SqsDocumentConsumer> _logger;

    public SqsDocumentConsumer(
        IAmazonSQS sqs,
        IServiceProvider services,
        IConfiguration config,
        ILogger<SqsDocumentConsumer> logger)
    {
        _sqs = sqs;
        _services = services;
        _queueUrl = config["Sqs:RequestQueueUrl"]
            ?? throw new InvalidOperationException("Sqs:RequestQueueUrl is not configured.");
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SqsDocumentConsumer started, polling {Queue}", _queueUrl);

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
                _logger.LogError(ex, "Polling error, retrying in 5s");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var request = new ReceiveMessageRequest
        {
            QueueUrl = _queueUrl,
            MaxNumberOfMessages = 5,
            WaitTimeSeconds = 20,
            VisibilityTimeout = 60,
            MessageAttributeNames = ["MessageType"]
        };

        var response = await _sqs.ReceiveMessageAsync(request, ct);

        var tasks = response.Messages.Select(m => ProcessAsync(m, ct));
        await Task.WhenAll(tasks);
    }

    private async Task ProcessAsync(Message sqsMessage, CancellationToken ct)
    {
        try
        {
            var @event = JsonSerializer.Deserialize<StatementRequestedEvent>(sqsMessage.Body);
            if (@event is null)
            {
                _logger.LogWarning("Could not deserialize SQS message {MessageId}", sqsMessage.MessageId);
                return;
            }

            _logger.LogInformation(
                "Processing StatementRequestedEvent CorrelationId={CorrelationId} DocumentId={DocumentId}",
                @event.CorrelationId, @event.DocumentId);

            await using var scope = _services.CreateAsyncScope();
            var documentService = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            await documentService.ProcessRequestAsync(@event, ct);

            await _sqs.DeleteMessageAsync(_queueUrl, sqsMessage.ReceiptHandle, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed processing SQS message {MessageId} — leaving for retry", sqsMessage.MessageId);
        }
    }
}
