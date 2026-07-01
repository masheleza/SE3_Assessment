using Amazon;
using Amazon.Runtime;
using Amazon.SQS;

namespace FinancialStatements.BFF.Infrastructure;

/// <summary>
/// Builds AWS service clients that target either real AWS or a local emulator
/// (LocalStack) depending on whether <c>AWS:ServiceURL</c> is configured.
/// When a ServiceURL is present, dummy credentials are supplied so the SDK does
/// not fall back to the EC2 instance-metadata credential provider.
/// </summary>
public static class AwsClientFactory
{
    public static IAmazonSQS CreateSqs(IConfiguration config)
    {
        var serviceUrl = config["AWS:ServiceURL"];
        var region = config["AWS:Region"] ?? "eu-west-1";

        var clientConfig = new AmazonSQSConfig();

        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            // LocalStack / emulator.
            clientConfig.ServiceURL = serviceUrl;
            clientConfig.AuthenticationRegion = region;
            var creds = new BasicAWSCredentials(
                config["AWS:AccessKey"] ?? "test",
                config["AWS:SecretKey"] ?? "test");
            return new AmazonSQSClient(creds, clientConfig);
        }

        clientConfig.RegionEndpoint = RegionEndpoint.GetBySystemName(region);
        return new AmazonSQSClient(clientConfig);
    }
}
