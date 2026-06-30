export type StatementType = 'Monthly' | 'Annual' | 'Transaction';
export type MessageType = 'Text' | 'SecureLink' | 'Notification' | 'Error';

export interface SecureLinkResponse {
  linkUrl: string;
  expiresAt: string;
  documentId: string;
}

export interface ChatMessage {
  id: string;
  userId: string;
  content: string;
  type: MessageType;
  secureLink?: SecureLinkResponse;
  sentAt: string;
}

export interface StatementRequest {
  accountId: string;
  type: StatementType;
  from: string;
  to: string;
}

export interface ConnectionState {
  isConnected: boolean;
  connectionId?: string;
  error?: string;
}

export interface LinkExpiry {
  token: string;
  minutesRemaining: number;
}
