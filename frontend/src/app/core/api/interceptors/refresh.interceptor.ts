import { Injectable } from '@angular/core';
import {
  HttpContextToken,
  HttpInterceptor,
  HttpRequest,
  HttpHandler,
  HttpEvent,
  HttpErrorResponse,
} from '@angular/common/http';
import { Observable, throwError, from } from 'rxjs';
import { catchError, finalize, shareReplay, switchMap } from 'rxjs/operators';
import { AuthTokenService } from '../services/auth-token.service';

/** Marks a request that has already been replayed once after a token refresh. */
export const RETRIED_AFTER_REFRESH = new HttpContextToken<boolean>(() => false);

@Injectable()
export class RefreshTokenInterceptor implements HttpInterceptor {
  private refresh$: Observable<void> | null = null;
  private readonly excludedPattern = '/auth/';

  constructor(private auth: AuthTokenService) {}

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    return next.handle(req).pipe(
      catchError((err: HttpErrorResponse) => {
        if (!this.shouldRefresh(req, err)) {
          return throwError(() => err);
        }

        return this.refreshOnce().pipe(
          switchMap(() => {
            const newToken = this.auth.accessToken;

            const retryReq = req.clone({
              setHeaders: newToken ? { Authorization: `Bearer ${newToken}` } : {},
              withCredentials: true,
              context: req.context.set(RETRIED_AFTER_REFRESH, true),
            });
            return next.handle(retryReq);
          }),
          catchError((innerErr) => {
            this.auth.logoutLocal();
            return throwError(() => innerErr);
          }),
        );
      }),
    );
  }

  private shouldRefresh(req: HttpRequest<any>, err: HttpErrorResponse): boolean {
    return (
      err.status === 401 &&
      !req.context.get(RETRIED_AFTER_REFRESH) &&
      !req.url.includes(this.excludedPattern)
    );
  }

  /**
   * Concurrent 401s share a single refresh instead of the first one winning and
   * the rest failing outright. `shareReplay` lets late subscribers join an
   * in-flight refresh; `finalize` clears the slot so the next 401 refreshes again.
   */
  private refreshOnce(): Observable<void> {
    if (!this.refresh$) {
      this.refresh$ = from(this.auth.refreshAccessToken()).pipe(
        finalize(() => {
          this.refresh$ = null;
        }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    }

    return this.refresh$;
  }
}
