namespace FinancialStatements.DocumentApi.Tests.Filters;

public sealed class GlobalExceptionHandlerTests
{
    private readonly Mock<ILogger<GlobalExceptionHandler>> _logger = new();
    private readonly GlobalExceptionHandler _sut;

    public GlobalExceptionHandlerTests() =>
        _sut = new GlobalExceptionHandler(_logger.Object);

    [Theory]
    [InlineData(typeof(InvalidOperationException), 400)]
    [InlineData(typeof(UnauthorizedAccessException), 401)]
    [InlineData(typeof(KeyNotFoundException), 404)]
    [InlineData(typeof(Exception), 500)]
    [InlineData(typeof(ArgumentException), 500)]
    public async Task TryHandleAsync_MapsExceptionToCorrectStatusCode(Type exceptionType, int expectedStatus)
    {
        var ctx = BuildHttpContext();
        var ex = (Exception)Activator.CreateInstance(exceptionType, "test message")!;

        await _sut.TryHandleAsync(ctx, ex, CancellationToken.None);

        ctx.Response.StatusCode.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task TryHandleAsync_AlwaysReturnsTrue()
    {
        var result = await _sut.TryHandleAsync(BuildHttpContext(), new Exception("err"), CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryHandleAsync_ResponseBodyContainsExceptionDetail()
    {
        var ctx = BuildHttpContext();

        await _sut.TryHandleAsync(ctx, new InvalidOperationException("operation not valid"), CancellationToken.None);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain("operation not valid");
    }

    [Fact]
    public async Task TryHandleAsync_ResponseBodyContainsStatusCode()
    {
        var ctx = BuildHttpContext();

        await _sut.TryHandleAsync(ctx, new KeyNotFoundException("not found"), CancellationToken.None);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain("404");
    }

    [Fact]
    public async Task TryHandleAsync_LogsErrorAtErrorLevel()
    {
        var ex = new Exception("boom");

        await _sut.TryHandleAsync(BuildHttpContext(), ex, CancellationToken.None);

        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                ex,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task TryHandleAsync_ResponseBodyIncludesTraceId()
    {
        var ctx = BuildHttpContext();
        ctx.TraceIdentifier = "trace-xyz";

        await _sut.TryHandleAsync(ctx, new Exception("err"), CancellationToken.None);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain("trace-xyz");
    }

    private static DefaultHttpContext BuildHttpContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }
}
