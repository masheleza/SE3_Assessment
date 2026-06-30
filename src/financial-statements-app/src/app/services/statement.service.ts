import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { SecureLinkResponse, StatementRequest } from '../models/statement.models';
import { SignalRService } from './signalr.service';

@Injectable({ providedIn: 'root' })
export class StatementService {
  private readonly baseUrl = `${environment.bffUrl}/api`;

  constructor(private http: HttpClient, private signalR: SignalRService) {}

  requestStatement(request: StatementRequest): Observable<SecureLinkResponse> {
    const headers = new HttpHeaders({
      'X-SignalR-ConnectionId': this.signalR.connectionId ?? ''
    });
    return this.http.post<SecureLinkResponse>(
      `${this.baseUrl}/statements/request`,
      request,
      { headers }
    );
  }

  downloadDocument(token: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/documents/download/${token}`, {
      responseType: 'blob'
    });
  }
}
