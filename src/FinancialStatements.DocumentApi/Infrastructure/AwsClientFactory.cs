using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SQS;

namespace FinancialStatements.DocumentApi.Infrastructure;

/// <summary>
/// Builds AWS service clients that target either real AWS or a local emulator
/// (LocalStack) depending on whether <c>AWS:ServiceURL</c> is configured.
/// When a ServiceURL is present, dummy credentials are supplied so the SDK does
/// not fall back to the EC2 instance-metadata credential provider, and S3 uses
/// path-style addressing (required by LocalStack).
/// </summary>
public static class AwsClientFactory
{
    private static (string? ServiceUrl, string Region, BasicAWSCredentials Creds) Read(IConfiguration config)
    {
        var region = config["AWS:Region"] ?? "eu-west-1";
        var creds = new BasicAWSCredentials(
            config["AWS:AccessKey"] ?? "test",
            config["AWS:SecretKey"] ?? "test");
        return (config["AWS:ServiceURL"], region, creds);
    }

    public static IAmazonSQS CreateSqs(IConfiguration config)
    {
        var (serviceUrl, region, creds) = Read(config);
        var clientConfig = new AmazonSQSConfig();

        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            clientConfig.ServiceURL = serviceUrl;
            clientConfig.AuthenticationRegion = region;
            return new AmazonSQSClient(creds, clientConfig);
        }

        clientConfig.RegionEndpoint = RegionEndpoint.GetBySystemName(region);
        return new AmazonSQSClient(clientConfig);
    }

    public static IAmazonS3 CreateS3(IConfiguration config)
    {
        var (serviceUrl, region, creds) = Read(config);
        var clientConfig = new AmazonS3Config();

        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            clientConfig.ServiceURL = serviceUrl;
            clientConfig.AuthenticationRegion = region;
            clientConfig.ForcePathStyle = true; // LocalStack requires path-style bucket addressing.
            return new AmazonS3Client(creds, clientConfig);
        }

        clientConfig.RegionEndpoint = RegionEndpoint.GetBySystemName(region);
        return new AmazonS3Client(clientConfig);
    }
}
