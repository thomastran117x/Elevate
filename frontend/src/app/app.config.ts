import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
  importProvidersFrom,
} from '@angular/core';
import { provideClientHydration, withEventReplay } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { CoreModule } from './core/core.module';
import { provideStore } from '@ngrx/store';
import { routes } from './app.routes';

import { userReducer } from './core/stores/user.reducer';

export const appConfig: ApplicationConfig = {
  providers: [
    provideStore({ user: userReducer }),
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideClientHydration(withEventReplay()),
    provideRouter(routes),
    importProvidersFrom(CoreModule),
  ],
};
