using Amazon.SQS;
using FinancialStatements.BFF.Delegates;
using FinancialStatements.BFF.Filters;
using FinancialStatements.BFF.Hubs;
using FinancialStatements.BFF.Infrastructure;
using FinancialStatements.BFF.Orchestrators;
using FinancialStatements.BFF.Services;
using FinancialStatements.BFF.Workers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── Global exception handler ──────────────────────────────────────────────────
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ── Infrastructure ────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

builder.Services.AddSingleton<ICacheService, RedisCacheService>();

builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonSQS>();
builder.Services.AddSingleton<ISqsPublisher, SqsPublisher>();

// ── Document API proxy ────────────────────────────────────────────────────────
builder.Services.AddHttpClient<IDocumentProxyService, DocumentProxyService>(http =>
    http.BaseAddress = new Uri(builder.Configuration["DocumentApi:BaseUrl"]!));

// ── Delegates (Delegate Design Pattern) ──────────────────────────────────────
builder.Services.AddScoped<IStatementDelegate, MonthlyStatementDelegate>();
builder.Services.AddScoped<IStatementDelegate, AnnualStatementDelegate>();
builder.Services.AddScoped<IStatementDelegate, TransactionStatementDelegate>();

// ── Orchestrator & Services ───────────────────────────────────────────────────
builder.Services.AddScoped<IStatementOrchestrator, StatementOrchestrator>();
builder.Services.AddScoped<ISecureLinkService, SecureLinkService>();
builder.Services.AddScoped<INotificationService, SignalRNotificationService>();

// ── Background Worker (SQS consumer for document-ready events) ────────────────
builder.Services.AddHostedService<DocumentReadyConsumer>();

// ── Authentication ─────────────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Jwt:Authority"];
        options.Audience = builder.Configuration["Jwt:Audience"];
        options.Events = new JwtBearerEvents
        {
            // Allow SignalR to pass JWT in query string
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ── SignalR ────────────────────────────────────────────────────────────────────
builder.Services.AddSignalR(opts =>
{
    opts.EnableDetailedErrors = builder.Environment.IsDevelopment();
    opts.KeepAliveInterval = TimeSpan.FromSeconds(15);
    opts.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

// ── Controllers & Swagger ─────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Financial Statements BFF", Version = "v1" });
});

// ── CORS ───────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()!;
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<StatementHub>("/hubs/statements");

app.Run();
