using Amazon.S3;
using Amazon.SQS;
using FinancialStatements.DocumentApi.Consumers;
using FinancialStatements.DocumentApi.Filters;
using FinancialStatements.DocumentApi.Infrastructure.DbContext;
using FinancialStatements.DocumentApi.Infrastructure.Repositories;
using FinancialStatements.DocumentApi.Services;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── Global exception handler ──────────────────────────────────────────────────
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ── Persistent layer (SQL Server via EF Core) ─────────────────────────────────
builder.Services.AddDbContext<DocumentDbContext>(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Cache (Redis) ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

// ── AWS ────────────────────────────────────────────────────────────────────────
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonSQS>();
builder.Services.AddAWSService<IAmazonS3>();

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<IDocumentStorageService, S3DocumentStorageService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();

// ── SQS Consumer (event-driven document processing) ───────────────────────────
builder.Services.AddHostedService<SqsDocumentConsumer>();

// ── Controllers & Swagger ─────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new() { Title = "Financial Statements Document API", Version = "v1" }));

var app = builder.Build();

app.UseExceptionHandler();

// Apply EF migrations at startup, and seed test data in Development
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
    await db.Database.MigrateAsync();

    if (app.Environment.IsDevelopment() && !await db.Documents.AnyAsync())
    {
        try
        {
            var seeder = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            await seeder.SeedTestStatementsAsync(10);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Development data seeding failed; continuing startup");
        }
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();
app.Run();
