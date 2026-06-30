import { Injectable, OnDestroy } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject, Subject } from 'rxjs';
import { environment } from '../../environments/environment';
import { ChatMessage, ConnectionState, LinkExpiry } from '../models/statement.models';

@Injectable({ providedIn: 'root' })
export class SignalRService implements OnDestroy {
  private hub!: signalR.HubConnection;

  readonly connectionState$ = new BehaviorSubject<ConnectionState>({ isConnected: false });
  readonly messages$ = new Subject<ChatMessage>();
  readonly linkExpiring$ = new Subject<LinkExpiry>();

  async connect(accessToken: string): Promise<void> {
    this.hub = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.bffUrl}/hubs/statements`, {
        accessTokenFactory: () => accessToken,
        transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (ctx) => {
          if (ctx.previousRetryCount < 3) return 2000;
          if (ctx.previousRetryCount < 6) return 5000;
          return 15000;
        }
      })
      .configureLogging(
        environment.production ? signalR.LogLevel.Warning : signalR.LogLevel.Information
      )
      .build();

    this.hub.on('ReceiveMessage', (message: ChatMessage) => {
      this.messages$.next(message);
    });

    this.hub.on('LinkExpiring', (token: string, minutesRemaining: number) => {
      this.linkExpiring$.next({ token, minutesRemaining });
    });

    this.hub.on('ConnectionEstablished', (connectionId: string) => {
      this.connectionState$.next({ isConnected: true, connectionId });
    });

    this.hub.onreconnecting(() => {
      this.connectionState$.next({ isConnected: false });
    });

    this.hub.onreconnected((connectionId) => {
      this.connectionState$.next({ isConnected: true, connectionId });
    });

    this.hub.onclose((err) => {
      this.connectionState$.next({ isConnected: false, error: err?.message });
    });

    await this.hub.start();
  }

  async sendMessage(content: string): Promise<void> {
    if (this.hub?.state !== signalR.HubConnectionState.Connected) return;
    await this.hub.invoke('SendMessage', content);
  }

  get connectionId(): string | undefined {
    return this.connectionState$.value.connectionId;
  }

  ngOnDestroy(): void {
    this.hub?.stop();
  }
}
