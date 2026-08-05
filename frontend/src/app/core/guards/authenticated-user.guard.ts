import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Store } from '@ngrx/store';
import { map, take } from 'rxjs/operators';

import { AuthReturnUrlService } from '../../features/auth/services/auth-return-url.service';
import { selectUser } from '../stores/user.selectors';

/**
 * Requires a signed-in visitor, remembering where they were headed so the login
 * page can send them back. Resource-level permissions (club ownership, event
 * management) are enforced server-side and surfaced as inline errors on the page.
 */
export const authenticatedUserGuard: CanActivateFn = (_route, state) => {
  const store = inject(Store);
  const router = inject(Router);
  const returnUrl = inject(AuthReturnUrlService);

  return store.select(selectUser).pipe(
    take(1),
    map((user) => {
      if (user) {
        return true;
      }

      returnUrl.set(state.url);
      return router.createUrlTree(['/auth/login'], {
        queryParams: { returnUrl: state.url },
      });
    }),
  );
};
