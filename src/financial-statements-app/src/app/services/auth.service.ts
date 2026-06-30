import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

interface LoginResponse {
  accessToken: string;
  tokenType: string;
  expiresAt: string;
}

/**
 * Holds the session access token. The app logs in once on startup
 * (see APP_INITIALIZER in app.config.ts) and the token is then reused
 * for the lifetime of the page session — kept in memory only, never persisted.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private token: string | null = null;
  private expiresAt: Date | null = null;

  constructor(private http: HttpClient) {}

  get accessToken(): string | null {
    return this.token;
  }

  get isAuthenticated(): boolean {
    return this.token !== null && (this.expiresAt === null || this.expiresAt > new Date());
  }

  /** Exchanges the configured Basic credentials for a signed JWT. */
  async login(): Promise<void> {
    const { username, password } = environment.auth;
    const basic = btoa(`${username}:${password}`);

    const response = await firstValueFrom(
      this.http.post<LoginResponse>(
        `${environment.bffUrl}/api/auth/login`,
        null,
        { headers: { Authorization: `Basic ${basic}` } }
      )
    );

    this.token = response.accessToken;
    this.expiresAt = new Date(response.expiresAt);
  }
}
