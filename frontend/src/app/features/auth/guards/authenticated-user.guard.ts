import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Store } from '@ngrx/store';
import { map, take } from 'rxjs/operators';

import { selectUser } from '../../../core/stores/user.selectors';
import { AuthReturnUrlService } from '../services/auth-return-url.service';

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
