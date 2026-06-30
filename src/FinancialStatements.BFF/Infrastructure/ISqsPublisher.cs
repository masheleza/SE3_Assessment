namespace FinancialStatements.BFF.Infrastructure;

public interface ISqsPublisher
{
    string ResponseQueueUrl { get; }
    Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class;
}
