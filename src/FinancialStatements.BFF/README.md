# FinancialStatements.BFF

Backend for Frontend (.NET 8). Owns the orchestration layer between the Angular SPA and the Document API, manages secure link lifecycle, and delivers real-time notifications via SignalR.

## Responsibilities

- **Orchestration** — receives statement requests and dispatches them to the correct `IStatementDelegate` via `StatementOrchestrator`.
- **Secure links** — generates one-time, time-limited download tokens backed by Redis. Validates and streams documents through a proxy endpoint so the Document API URL is never exposed to the browser.
- **Real-time** — hosts a SignalR hub (`/hubs/statements`) and notifies users when their document is ready.
- **Event publishing** — publishes `StatementRequestedEvent` to the SQS request queue.
- **Event consumption** — `DocumentReadyConsumer` (BackgroundService) polls the SQS response queue and triggers SignalR notifications.

## Project structure

```
Controllers/
  StatementController.cs      POST /api/statements/request
  DocumentController.cs       GET  /api/documents/download/{token}
Delegates/
  IStatementDelegate.cs       Contract: CanHandle + ExecuteAsync
  MonthlyStatementDelegate.cs
  AnnualStatementDelegate.cs
  TransactionStatementDelegate.cs
Filters/
  GlobalExceptionHandler.cs   IExceptionHandler — maps exceptions to ProblemDetails
Hubs/
  StatementHub.cs             SignalR hub with typed IStatementHubClient
Infrastructure/
  CacheService.cs             Redis wrapper (ICacheService / RedisCacheService)
  ISqsPublisher.cs
  SqsPublisher.cs             Publishes events to the SQS request queue
Models/
  StatementModels.cs          Internal BFF domain objects: StatementRequest, StatementResult, SecureLink
Orchestrators/
  StatementOrchestrator.cs    Delegate design pattern — resolves and calls delegate
Services/
  SecureLinkService.cs        GUID token generation, validation, single-use marking
  NotificationService.cs      SignalR push via IHubContext
  DocumentProxyService.cs     HttpClient proxy to the Document API
Workers/
  DocumentReadyConsumer.cs    BackgroundService polling SQS response queue
```

Shared enums, request/response DTOs, and domain events live in the `FinancialStatements.Models` library (see its README). `Models/StatementModels.cs` contains only internal BFF records that are never serialised over the wire.

## Delegate pattern

`StatementOrchestrator` holds an `IEnumerable<IStatementDelegate>`. When a request arrives it calls `FirstOrDefault(d => d.CanHandle(type))` and delegates execution. Each concrete delegate publishes a typed SQS event and returns a `StatementResult` containing the new `DocumentId`.

```
StatementOrchestrator
  └─► IStatementDelegate.CanHandle(type) → true
        └─► ExecuteAsync(request) → StatementResult(DocumentId)
  └─► SecureLinkService.GenerateAsync(documentId) → SecureLinkResponseDto
```

Adding a new statement type requires only a new class and a new `StatementType` enum value in the shared Models library — the orchestrator requires no changes.

## Secure link lifecycle

```
GenerateAsync  →  store { token → SecureLink } in Redis (TTL = 30 min)
ValidateAsync  →  read from Redis; null = expired or not found
MarkUsedAsync  →  set IsUsed = true; retain remaining TTL
Download endpoint enforces: exists AND !IsUsed AND !Expired
```

## Exception handling

`GlobalExceptionHandler` implements `IExceptionHandler` and is registered in `Program.cs` via `AddExceptionHandler<GlobalExceptionHandler>()`. All unhandled exceptions are:

1. Logged at `Error` level with the full exception and request context.
2. Mapped to an HTTP status code.
3. Returned as `ProblemDetails` JSON with a `traceId` field for correlation.

| Exception | Status |
|---|---|
| `InvalidOperationException` | 400 |
| `UnauthorizedAccessException` | 401 |
| `KeyNotFoundException` | 404 |
| All others | 500 |

## SignalR

The hub is at `/hubs/statements`. The JWT is expected either in the `Authorization` header or as a `?access_token=` query parameter (required for WebSocket connections from browsers).

Typed client interface:

```csharp
Task ReceiveMessage(ChatMessageDto message, ct);
Task LinkExpiring(string token, int minutesRemaining, ct);
Task ConnectionEstablished(string connectionId, ct);
```

## Configuration (`appsettings.json`)

```json
{
  "ConnectionStrings": { "Redis": "localhost:6379" },
  "Sqs": {
    "RequestQueueUrl": "...",
    "ResponseQueueUrl": "..."
  },
  "SecureLink": { "Expiry": "00:30:00" },
  "BFF": { "BaseUrl": "https://localhost:7001" },
  "DocumentApi": { "BaseUrl": "https://localhost:7002" },
  "Jwt": { "Authority": "...", "Audience": "financial-statements-bff" },
  "Cors": { "AllowedOrigins": ["http://localhost:4200"] }
}
```

## Running locally

```bash
dotnet run
# Swagger: https://localhost:7001/swagger
```

Requires Redis and the SQS queues to be available. See the root README for infrastructure setup.

## Running tests

```bash
dotnet test ../FinancialStatements.BFF.Tests
```

Test coverage includes: delegate dispatch (all three types), orchestrator error paths, secure link generation and validation, `DocumentReadyConsumer` SQS polling, `StatementController` and `DocumentController` action results, and the `GlobalExceptionHandler` status-code mapping and ProblemDetails body.

## Dependencies

| Package | Purpose |
|---|---|
| `FinancialStatements.Models` | Shared enums, DTOs, and domain events |
| `AWSSDK.SQS` | Publish to request queue; poll response queue |
| `Microsoft.AspNetCore.SignalR` | Real-time hub |
| `StackExchange.Redis` | Secure link token store and cache |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | JWT validation |
| `Swashbuckle.AspNetCore` | Swagger UI |
