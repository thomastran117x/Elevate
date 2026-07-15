import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Store } from '@ngrx/store';
import { map, take } from 'rxjs/operators';

import { AuthReturnUrlService } from '../../auth/services/auth-return-url.service';
import { selectUser } from '../../../core/stores/user.selectors';

/**
 * Guards the owner-side club management routes. Only checks that the visitor is
 * signed in — per-club ownership/manage permissions are enforced server-side and
 * surfaced to the user as inline errors on the management pages.
 */
export const clubManageAuthGuard: CanActivateFn = (_route, state) => {
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
