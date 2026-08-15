import { NgModule } from '@angular/core';
import {
  HTTP_INTERCEPTORS,
  provideHttpClient,
  withFetch,
  withInterceptorsFromDi,
} from '@angular/common/http';
import { AccessTokenInterceptor } from './interceptors/access.interceptor';
import { RefreshTokenInterceptor } from './interceptors/refresh.interceptor';
import { CsrfInterceptor } from './interceptors/csrf.interceptor';

@NgModule({
  providers: [
    provideHttpClient(withFetch(), withInterceptorsFromDi()),
    { provide: HTTP_INTERCEPTORS, useClass: AccessTokenInterceptor, multi: true },
    { provide: HTTP_INTERCEPTORS, useClass: RefreshTokenInterceptor, multi: true },
    { provide: HTTP_INTERCEPTORS, useClass: CsrfInterceptor, multi: true },
  ],
})
export class ApiModule {}
