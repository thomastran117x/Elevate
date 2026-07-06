import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
  importProvidersFrom,
} from '@angular/core';
import {
  provideClientHydration,
  withEventReplay,
  withNoHttpTransferCache,
} from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { CoreModule } from './core/core.module';
import { provideStore } from '@ngrx/store';
import { routes } from './app.routes';

import { userReducer } from './core/stores/user.reducer';
import { sessionReducer } from './core/stores/session.reducer';

export const appConfig: ApplicationConfig = {
  providers: [
    provideStore({ user: userReducer, session: sessionReducer }),
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    // Disable the SSR HTTP transfer cache so authenticated backend GETs are always
    // re-issued from the browser (visible in the Network tab) rather than replayed
    // from server-rendered TransferState.
    provideClientHydration(withEventReplay(), withNoHttpTransferCache()),
    provideRouter(routes),
    importProvidersFrom(CoreModule),
  ],
};
