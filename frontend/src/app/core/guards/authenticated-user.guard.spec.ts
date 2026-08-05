import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { Observable, firstValueFrom } from 'rxjs';

import { makeCurrentUser, provideTestStore } from '@testing';

import { authenticatedUserGuard } from './authenticated-user.guard';
import { AuthReturnUrlService } from '../../features/auth/services/auth-return-url.service';
import { User } from '../stores/user.model';

describe('authenticatedUserGuard', () => {
  let returnUrl: { set: jasmine.Spy };

  function runGuard(user: User | null, url = '/clubs/2/manage'): Promise<boolean | UrlTree> {
    returnUrl = { set: jasmine.createSpy('set') };

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        ...provideTestStore({ user }),
        { provide: AuthReturnUrlService, useValue: returnUrl },
      ],
    });

    const result = TestBed.runInInjectionContext(() =>
      authenticatedUserGuard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
    ) as Observable<boolean | UrlTree>;

    return firstValueFrom(result);
  }

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('lets a signed-in visitor through', async () => {
    await expectAsync(runGuard(makeCurrentUser())).toBeResolvedTo(true);
    expect(returnUrl.set).not.toHaveBeenCalled();
  });

  it('redirects a signed-out visitor to the login page', async () => {
    const result = await runGuard(null);

    expect(result).toEqual(jasmine.any(UrlTree));
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe(
      '/auth/login?returnUrl=%2Fclubs%2F2%2Fmanage',
    );
  });

  it('remembers where the signed-out visitor was headed', async () => {
    await runGuard(null, '/events/7/waitlist/manage');

    expect(returnUrl.set).toHaveBeenCalledOnceWith('/events/7/waitlist/manage');
  });
});
