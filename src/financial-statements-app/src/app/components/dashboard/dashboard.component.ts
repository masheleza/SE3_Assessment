import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject, takeUntil } from 'rxjs';
import { SignalRService } from '../../services/signalr.service';
import { ChatComponent } from '../chat/chat.component';
import { ConnectionState } from '../../models/statement.models';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, ChatComponent],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.scss']
})
export class DashboardComponent implements OnInit, OnDestroy {
  connectionState: ConnectionState = { isConnected: false };
  private readonly destroy$ = new Subject<void>();

  constructor(private signalR: SignalRService) {}

  ngOnInit(): void {
    this.signalR.connectionState$
      .pipe(takeUntil(this.destroy$))
      .subscribe(state => (this.connectionState = state));

    // In production, retrieve token from your AuthService
    const mockToken = 'your-jwt-token-here';
    this.signalR.connect(mockToken).catch(console.error);
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
