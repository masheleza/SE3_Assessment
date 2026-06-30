import { Component, EventEmitter, Input, OnDestroy, OnInit, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SecureLinkResponse } from '../../models/statement.models';

@Component({
  selector: 'app-secure-link',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './secure-link.component.html',
  styleUrls: ['./secure-link.component.scss']
})
export class SecureLinkComponent implements OnInit, OnDestroy {
  @Input({ required: true }) link!: SecureLinkResponse;
  @Input() expiryWarning?: number;
  @Output() download = new EventEmitter<SecureLinkResponse>();

  timeRemaining = '';
  isExpired = false;
  private timer?: ReturnType<typeof setInterval>;

  ngOnInit(): void {
    this.startCountdown();
  }

  private startCountdown(): void {
    this.tick();
    this.timer = setInterval(() => this.tick(), 1000);
  }

  private tick(): void {
    const now = Date.now();
    const expires = new Date(this.link.expiresAt).getTime();
    const diff = expires - now;

    if (diff <= 0) {
      this.isExpired = true;
      this.timeRemaining = 'Expired';
      clearInterval(this.timer);
      return;
    }

    const minutes = Math.floor(diff / 60000);
    const seconds = Math.floor((diff % 60000) / 1000);
    this.timeRemaining = `${minutes}m ${seconds.toString().padStart(2, '0')}s`;
  }

  onDownload(): void {
    if (!this.isExpired) this.download.emit(this.link);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }
}
