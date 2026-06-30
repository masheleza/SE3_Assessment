import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject, takeUntil } from 'rxjs';
import { SignalRService } from '../../services/signalr.service';
import { AuthService } from '../../services/auth.service';
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
  menuOpen = false;
  private readonly destroy$ = new Subject<void>();

  constructor(private signalR: SignalRService, private auth: AuthService) {}

  toggleMenu(): void {
    this.menuOpen = !this.menuOpen;
  }

  closeMenu(): void {
    this.menuOpen = false;
  }

  ngOnInit(): void {
    this.signalR.connectionState$
      .pipe(takeUntil(this.destroy$))
      .subscribe(state => (this.connectionState = state));

    // The app logs in on startup (APP_INITIALIZER), so the token is ready here.
    const token = this.auth.accessToken;
    if (token) {
      this.signalR.connect(token).catch(console.error);
    } else {
      console.error('No access token available; SignalR connection skipped.');
    }
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
