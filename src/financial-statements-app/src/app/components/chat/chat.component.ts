import {
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  ViewChild
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { ChatMessage, LinkExpiry, StatementType } from '../../models/statement.models';
import { SignalRService } from '../../services/signalr.service';
import { StatementService } from '../../services/statement.service';
import { SecureLinkComponent } from '../secure-link/secure-link.component';

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, SecureLinkComponent],
  templateUrl: './chat.component.html',
  styleUrls: ['./chat.component.scss']
})
export class ChatComponent implements OnInit, OnDestroy {
  @ViewChild('messageList') private messageList!: ElementRef<HTMLElement>;

  messages: ChatMessage[] = [];
  expiringLinks = new Map<string, number>();
  statementForm!: FormGroup;
  isRequestingStatement = false;

  readonly statementTypes: StatementType[] = ['Monthly', 'Annual', 'Transaction'];

  private readonly destroy$ = new Subject<void>();

  constructor(
    private fb: FormBuilder,
    private signalR: SignalRService,
    private statementService: StatementService
  ) {}

  ngOnInit(): void {
    this.buildForm();
    this.subscribeToMessages();
    this.subscribeToLinkExpiry();
  }

  private buildForm(): void {
    const today = new Date();
    const firstOfMonth = new Date(today.getFullYear(), today.getMonth(), 1);

    this.statementForm = this.fb.group({
      accountId: ['ACC-001', Validators.required],
      type: ['Monthly' as StatementType, Validators.required],
      from: [firstOfMonth.toISOString().substring(0, 10), Validators.required],
      to: [today.toISOString().substring(0, 10), Validators.required]
    });
  }

  private subscribeToMessages(): void {
    this.signalR.messages$
      .pipe(takeUntil(this.destroy$))
      .subscribe(msg => {
        this.messages.push(msg);
        this.scrollToBottom();
      });
  }

  private subscribeToLinkExpiry(): void {
    this.signalR.linkExpiring$
      .pipe(takeUntil(this.destroy$))
      .subscribe(({ token, minutesRemaining }: LinkExpiry) => {
        this.expiringLinks.set(token, minutesRemaining);
      });
  }

  requestStatement(): void {
    if (this.statementForm.invalid || this.isRequestingStatement) return;

    this.isRequestingStatement = true;
    const { accountId, type, from, to } = this.statementForm.value;

    const userMsg: ChatMessage = {
      id: crypto.randomUUID(),
      userId: 'me',
      content: `Requesting ${type} statement for ${accountId} (${from} – ${to})`,
      type: 'Text',
      sentAt: new Date().toISOString()
    };
    this.messages.push(userMsg);
    this.scrollToBottom();

    this.statementService
      .requestStatement({ accountId, type, from, to })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.isRequestingStatement = false;
          // Real-time notification arrives via SignalR (DocumentReadyConsumer → SignalR Hub)
        },
        error: () => {
          this.isRequestingStatement = false;
          this.showError(
            "We couldn't submit your statement request right now. Please try again in a moment."
          );
        }
      });
  }

  downloadLink(link: { linkUrl: string; documentId: string; expiresAt: string }): void {
    const token = link.linkUrl.split('/').pop()!;
    this.statementService
      .downloadDocument(token)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (blob) => {
          const url = URL.createObjectURL(blob);
          const a = document.createElement('a');
          a.href = url;
          a.download = `statement-${link.documentId}.pdf`;
          a.click();
          URL.revokeObjectURL(url);
        },
        error: () => {
          this.showError(
            "We couldn't retrieve your statement right now. The link may have expired. Please request a new statement and try again."
          );
        }
      });
  }

  private showError(message: string): void {
    const errMsg: ChatMessage = {
      id: crypto.randomUUID(),
      userId: 'system',
      content: message,
      type: 'Error',
      sentAt: new Date().toISOString()
    };
    this.messages.push(errMsg);
    this.scrollToBottom();
  }

  private scrollToBottom(): void {
    setTimeout(() => {
      const el = this.messageList?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    }, 50);
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
