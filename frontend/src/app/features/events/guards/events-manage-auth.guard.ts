import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Store } from '@ngrx/store';
import { map, take } from 'rxjs/operators';

import { AuthReturnUrlService } from '../../auth/services/auth-return-url.service';
import { selectUser } from '../../../core/stores/user.selectors';

export const eventsManageAuthGuard: CanActivateFn = (_route, state) => {
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
