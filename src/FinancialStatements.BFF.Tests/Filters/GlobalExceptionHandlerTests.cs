namespace FinancialStatements.BFF.Tests.Filters;

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
    [InlineData(typeof(NotSupportedException), 500)]
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
        var ctx = BuildHttpContext();

        var result = await _sut.TryHandleAsync(ctx, new Exception("boom"), CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryHandleAsync_WritesProblemDetailsJson()
    {
        var ctx = BuildHttpContext();

        await _sut.TryHandleAsync(ctx, new InvalidOperationException("bad state"), CancellationToken.None);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();

        body.Should().Contain("bad state");
        body.Should().Contain("400");
    }

    [Fact]
    public async Task TryHandleAsync_ProblemDetailsIncludesTraceId()
    {
        var ctx = BuildHttpContext();
        ctx.TraceIdentifier = "trace-abc";

        await _sut.TryHandleAsync(ctx, new Exception("err"), CancellationToken.None);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain("trace-abc");
    }

    [Fact]
    public async Task TryHandleAsync_LogsErrorWithExceptionDetails()
    {
        var ctx = BuildHttpContext();
        var ex = new InvalidOperationException("bad input");

        await _sut.TryHandleAsync(ctx, ex, CancellationToken.None);

        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("InvalidOperationException")),
                ex,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static DefaultHttpContext BuildHttpContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }
}
