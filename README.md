# Financial Statements

Secure financial statement retrieval portal. Users request statements via a chat interface; a time-limited secure link is delivered in real time via SignalR and expires after 30 minutes.

## Architecture

```
┌──────────────── Angular 17 SPA (port 4200) ─────────────────┐
│  Dashboard │ Chat + Secure Link card │ SignalR client        │
└───────────────────────┬─────────────────────────────────────┘
                        │ HTTP / WebSocket
┌───────────────────────▼──── BFF .NET 8 (port 7001) ─────────┐
│  StatementOrchestrator → IStatementDelegate chain            │
│  SecureLinkService  →  Redis (GUID token + TTL)              │
│  StatementHub  ←  DocumentReadyConsumer (SQS BackgroundSvc)  │
│  SqsPublisher  →  SQS request queue                         │
└──────────────────────────────────────────────────────────────┘
          │ SQS request               │ SQS response
┌─────────▼──── Document API .NET 8 (port 7002) ──────────────┐
│  SqsDocumentConsumer (BackgroundService)                     │
│  DocumentService  →  Redis cache-aside  →  SQL Server (EF)  │
│  S3DocumentStorageService  →  AWS S3                        │
└──────────────────────────────────────────────────────────────┘
```

### Event flow

1. User submits a statement request in the chat UI.
2. BFF dispatches to the matching `IStatementDelegate`, publishes a `StatementRequestedEvent` to SQS, and returns `202 Accepted`.
3. Document API's `SqsDocumentConsumer` picks up the event, generates the PDF, uploads to S3, caches in Redis, then publishes a `DocumentReadyEvent` to the response queue.
4. BFF's `DocumentReadyConsumer` picks up the response, mints a 30-minute secure link (GUID token stored in Redis), and pushes a chat message to the user via SignalR.
5. The user sees a countdown card. Clicking **Download** validates the token, marks it used, proxies the file from the Document API, and streams the PDF.

## Projects

| Path | Description |
|---|---|
| `src/FinancialStatements.Models` | Shared class library — enums, request/response DTOs, domain events |
| `src/FinancialStatements.BFF` | Backend for Frontend — orchestrator, SignalR hub, secure links |
| `src/FinancialStatements.BFF.Tests` | xUnit unit tests for the BFF |
| `src/FinancialStatements.DocumentApi` | Document API — SQS consumer, EF Core persistence, Redis cache, S3 storage |
| `src/FinancialStatements.DocumentApi.Tests` | xUnit unit tests for the Document API |
| `src/financial-statements-app` | Angular 17 SPA — chat UI, secure-link component, SignalR client |

## Prerequisites

- .NET 8 SDK
- Node.js 20+ / npm
- Docker Desktop (for local infrastructure)
- AWS CLI (for LocalStack queue/bucket setup)

## Quick start

### 1. Start infrastructure

1. Pull Images 

docker pull redis:latest

docker pull mcr.microsoft.com/mssql/server:2022-latest

docker pull localstack/localstack:latest

2. Run Redis
docker run -d --name redis -p 6379:6379 redis:latest

Verify:

docker ps

3. Run SQL Server
docker run -d --name sqlserver -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=YourStrong@Pass123" -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest

Connect using:

Server: localhost,1433
User: sa
Password: YourStrong@Pass123

4. Run LocalStack (SQS + S3)
docker run -d --name localstack -p 4566:4566 -e SERVICES=sqs,s3 -e DEBUG=1 localstack/localstack:latest

```bash
docker-compose up -d redis sqlserver localstack
```

### 2. Create SQS queues and S3 bucket (LocalStack)

```bash
aws --endpoint-url=http://localhost:4566 --region eu-west-1 \
  sqs create-queue --queue-name financial-statements-requests

aws --endpoint-url=http://localhost:4566 --region eu-west-1 \
  sqs create-queue --queue-name financial-statements-responses

aws --endpoint-url=http://localhost:4566 --region eu-west-1 \
  s3 mb s3://financial-statements-documents
```

### 3. Run the backends

```bash
# Terminal 1
cd src/FinancialStatements.DocumentApi
dotnet run

# Terminal 2
cd src/FinancialStatements.BFF
dotnet run
```

### 4. Run the frontend

```bash
cd src/financial-statements-app
npm install
npm start
```

Open `http://localhost:4200`.

### Run everything with Docker Compose

```bash
docker-compose up --build
```

## Running tests

```bash
# BFF tests
dotnet test src/FinancialStatements.BFF.Tests

# Document API tests
dotnet test src/FinancialStatements.DocumentApi.Tests

# All tests
dotnet test
```

## Configuration

Each project has its own `appsettings.json`. Override with environment variables in Docker Compose or your deployment platform. Key settings:

| Setting | Default | Description |
|---|---|---|
| `Sqs:RequestQueueUrl` | LocalStack URL | SQS queue for document requests |
| `Sqs:ResponseQueueUrl` | LocalStack URL | SQS queue for document-ready events |
| `SecureLink:Expiry` | `00:30:00` | Link TTL (hh:mm:ss) |
| `Storage:BucketName` | `financial-statements-documents` | S3 bucket |
| `Jwt:Authority` | — | OIDC authority URL |

## Exception handling

Both APIs use a `GlobalExceptionHandler` (`IExceptionHandler`) registered in `Program.cs`. All unhandled exceptions are caught, logged at `Error` level, and returned as [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) `ProblemDetails` JSON. The `traceId` field in the response body matches the ASP.NET Core `HttpContext.TraceIdentifier` for correlation.

| Exception type | HTTP status |
|---|---|
| `InvalidOperationException` | 400 Bad Request |
| `UnauthorizedAccessException` | 401 Unauthorized |
| `KeyNotFoundException` | 404 Not Found |
| All others | 500 Internal Server Error |

## Extending statement types

1. Add the new value to `StatementType` in `src/FinancialStatements.Models/Enums/StatementType.cs`.
2. Create a class that implements `IStatementDelegate` in `src/FinancialStatements.BFF/Delegates/`.
3. Register it in `Program.cs`: `builder.Services.AddScoped<IStatementDelegate, YourDelegate>();`

The orchestrator picks up the new delegate automatically — no changes needed to `StatementOrchestrator`.
