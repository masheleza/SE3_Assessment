# financial-statements-app

Angular 17 single-page application. Provides a chat-style interface for requesting financial statements, receives real-time notifications via SignalR, and presents time-limited secure download links with a live countdown.

## Features

- Chat interface with statement request form (type, account, date range).
- Real-time document-ready notifications pushed from the BFF via SignalR.
- Secure link card with live countdown timer, expiry warning (pulses when ≤ 5 minutes remain), and single-click download.
- Automatic SignalR reconnection with exponential back-off.
- HTTP interceptor attaches JWT Bearer token to every outbound API request.
- Dev proxy routes `/api` and `/hubs` (WebSocket) to the BFF, avoiding CORS during development.

## Project structure

```
src/app/
  components/
    dashboard/          Shell layout: header, sidebar nav, connection status indicator
    chat/               Message list + statement request form
    secure-link/        Countdown card — timer, expiry warning, download button
  interceptors/
    auth.interceptor.ts Attaches Authorization: Bearer <token> to HTTP requests
  models/
    statement.models.ts Shared TypeScript interfaces and union types
  services/
    signalr.service.ts  HubConnection lifecycle, reconnect policy, event streams
    statement.service.ts API calls: request statement, download document
  app.component.ts      Root standalone component
  app.config.ts         provideRouter + provideHttpClient with interceptors
  app.routes.ts         Single route → DashboardComponent
src/environments/
  environment.ts        { bffUrl: 'https://localhost:7001' }
  environment.prod.ts   Production BFF URL
proxy.conf.json         Dev proxy for /api and /hubs (WebSocket-aware)
```

## Getting started

```bash
npm install
npm start          # ng serve with proxy → http://localhost:4200
npm run build      # production build → dist/
npm test           # karma unit tests
```

The BFF must be running at `https://localhost:7001` (or the URL in `environment.ts`).

## SignalR connection

`SignalRService.connect(accessToken)` is called from `DashboardComponent.ngOnInit`. In production, replace the mock token with a token from your OIDC library (e.g. `angular-oauth2-oidc`):

```ts
// dashboard.component.ts
const token = await this.authService.getAccessToken();
await this.signalR.connect(token);
```

The service exposes three observables:

| Observable | Emits |
|---|---|
| `connectionState$` | `{ isConnected, connectionId?, error? }` — drives the status dot in the header |
| `messages$` | `ChatMessage` — new messages (text, secure link, error) |
| `linkExpiring$` | `{ token, minutesRemaining }` — triggers the warning animation on the link card |

Reconnect delays: 2 s × 3, then 5 s × 3, then 15 s indefinitely.

## Secure link card

`SecureLinkComponent` is a standalone component that:

- Starts a 1-second `setInterval` countdown from `link.expiresAt`.
- Applies `.secure-link--expiring` (orange pulse animation) when `minutesRemaining ≤ 5`.
- Applies `.secure-link--expired` and disables the button when time runs out.
- Emits a `(download)` output event; the parent `ChatComponent` calls `StatementService.downloadDocument(token)` and triggers a browser file download via a temporary object URL.

## API integration

The Angular app communicates exclusively with the BFF. The `FinancialStatements.Models` shared library and the Document API are backend concerns — the SPA has no direct knowledge of them.

### Request a statement

```
POST /api/statements/request
Headers: Authorization: Bearer <token>
         X-SignalR-ConnectionId: <connectionId>
Body:    { accountId, type, from, to }
→ 202 Accepted  { linkUrl, expiresAt, documentId }
```

The provisional link in the `202` response is not yet active — the real, ready-to-use link arrives via SignalR once the Document API finishes processing.

### Download a document

```
GET /api/documents/download/{token}
→ 200  application/pdf
→ 404  token not found or expired
→ 410  token already used
```

## Dev proxy

`proxy.conf.json` forwards:

- `/api/**` → `https://localhost:7001` (HTTP)
- `/hubs/**` → `https://localhost:7001` (WebSocket, `ws: true`)

This is applied automatically by `angular.json` when running `npm start`.

## Environment variables

| Key | Development | Production |
|---|---|---|
| `bffUrl` | `https://localhost:7001` | Your deployed BFF URL |

## Dependencies

| Package | Purpose |
|---|---|
| `@microsoft/signalr` | SignalR WebSocket / long-polling client |
| `@angular/forms` | Reactive form for the statement request panel |
| `@angular/common/http` | HTTP client with interceptor support |
