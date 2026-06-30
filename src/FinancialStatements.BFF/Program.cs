using Amazon.SQS;
using FinancialStatements.BFF.Delegates;
using FinancialStatements.BFF.Filters;
using FinancialStatements.BFF.Hubs;
using FinancialStatements.BFF.Infrastructure;
using FinancialStatements.BFF.Orchestrators;
using FinancialStatements.BFF.Services;
using FinancialStatements.BFF.Workers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
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

// ── Authentication (self-issued RS256 JWT) ──────────────────────────────────────
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddSingleton(jwtOptions);

var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
builder.Services.AddSingleton(authOptions);

// Single token-service instance shared by DI and the JwtBearer validation setup.
IJwtTokenService tokenService = new JwtTokenService(jwtOptions, builder.Environment);
builder.Services.AddSingleton(tokenService);
builder.Services.AddSingleton<IPasswordHasher<AuthUser>, PasswordHasher<AuthUser>>();
builder.Services.AddSingleton<IUserStore, ConfigurationUserStore>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Tokens are signed/validated locally with our RSA key pair.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = tokenService.Issuer,
            ValidateAudience = true,
            ValidAudience = tokenService.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = tokenService.PublicSigningKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtTokenService.NameClaimType
        };

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

    // Enable Bearer auth in Swagger UI so protected endpoints are testable.
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Paste the access token returned by POST /api/auth/login."
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
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
