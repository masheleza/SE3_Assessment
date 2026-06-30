using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace FinancialStatements.BFF.Infrastructure;

public sealed class SqsPublisher : ISqsPublisher
{
    private readonly IAmazonSQS _sqs;
    private readonly string _requestQueueUrl;
    private readonly ILogger<SqsPublisher> _logger;

    public string ResponseQueueUrl { get; }

    public SqsPublisher(IAmazonSQS sqs, IConfiguration config, ILogger<SqsPublisher> logger)
    {
        _sqs = sqs;
        _requestQueueUrl = config["Sqs:RequestQueueUrl"]
            ?? throw new InvalidOperationException("Sqs:RequestQueueUrl is not configured.");
        ResponseQueueUrl = config["Sqs:ResponseQueueUrl"]
            ?? throw new InvalidOperationException("Sqs:ResponseQueueUrl is not configured.");
        _logger = logger;
    }

    public async Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class
    {
        var body = JsonSerializer.Serialize(message);

        var request = new SendMessageRequest
        {
            QueueUrl = _requestQueueUrl,
            MessageBody = body,
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["MessageType"] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = typeof(T).Name
                }
            }
        };

        var response = await _sqs.SendMessageAsync(request, ct);
        _logger.LogDebug("SQS message sent. MessageId={MessageId}", response.MessageId);
    }
}
