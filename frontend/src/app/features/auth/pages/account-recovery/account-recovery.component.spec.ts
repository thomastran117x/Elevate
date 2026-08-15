import { convertToParamMap } from '@angular/router';
import { fakeAsync, tick } from '@angular/core/testing';
import { of } from 'rxjs';

import { AuthService } from '../../services/auth.service';
import { RecaptchaV3Service } from '../../services/recaptcha.service';
import { AccountRecoveryComponent } from './account-recovery.component';

describe('AccountRecoveryComponent', () => {
  function create(mode = 'password') {
    const auth = jasmine.createSpyObj<AuthService>('AuthService', [
      'recoverPassword',
      'recoverUsername',
    ]);
    const recaptcha = jasmine.createSpyObj<RecaptchaV3Service>('RecaptchaV3Service', ['execute']);
    const route = {
      snapshot: { queryParamMap: convertToParamMap({ mode }) },
    };
    const router = jasmine.createSpyObj('Router', ['navigate']);
    router.navigate.and.resolveTo(true);
    const component = new AccountRecoveryComponent(auth, recaptcha, route as any, router);
    component.ngOnInit();
    return { component, auth, recaptcha, router };
  }

  it('starts password recovery by username and opens the code form', fakeAsync(() => {
    const { component, auth, recaptcha, router } = create();
    recaptcha.execute.and.resolveTo('captcha-token');
    auth.recoverPassword.and.returnValue(
      of({
        success: true,
        message: 'ok',
        data: { Challenge: 'otp-challenge', ExpiresAtUtc: '2026-08-15T12:00:00Z' },
      } as any),
    );
    component.passwordForm.setValue({ username: ' member-user ' });

    void component.submitPasswordRecovery();
    tick();

    expect(recaptcha.execute).toHaveBeenCalledWith(component.siteKey, 'recover_password');
    expect(auth.recoverPassword).toHaveBeenCalledWith({
      username: 'member-user',
      captcha: 'captcha-token',
    });
    expect(router.navigate).toHaveBeenCalledWith(['/auth/reset-password'], {
      queryParams: { challenge: 'otp-challenge' },
    });
  }));

  it('uses a generic success message for username recovery', fakeAsync(() => {
    const { component, auth, recaptcha } = create('username');
    recaptcha.execute.and.resolveTo('captcha-token');
    auth.recoverUsername.and.returnValue(of(undefined));
    component.usernameForm.setValue({ email: ' member@example.com ' });

    void component.submitUsernameRecovery();
    tick();

    expect(auth.recoverUsername).toHaveBeenCalledWith({
      email: 'member@example.com',
      captcha: 'captcha-token',
    });
    expect(component.success).toBe('If that account exists, recovery instructions have been sent.');
  }));
});
