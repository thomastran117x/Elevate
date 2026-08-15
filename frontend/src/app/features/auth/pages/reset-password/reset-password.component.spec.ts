import { convertToParamMap } from '@angular/router';
import { of } from 'rxjs';

import { AuthService } from '../../services/auth.service';
import { ResetPasswordComponent } from './reset-password.component';

describe('ResetPasswordComponent', () => {
  function create(queryParams: Record<string, string>) {
    const auth = jasmine.createSpyObj<AuthService>('AuthService', ['resetPassword']);
    const route = {
      snapshot: { queryParamMap: convertToParamMap(queryParams) },
    };
    const router = jasmine.createSpyObj('Router', ['navigate']);
    router.navigate.and.resolveTo(true);
    const component = new ResetPasswordComponent(auth, route as any, router);
    component.ngOnInit();
    auth.resetPassword.and.returnValue(of(undefined));
    return { component, auth };
  }

  it('submits an emailed link token', () => {
    const { component, auth } = create({ token: 'reset-token' });
    component.form.setValue({
      code: '',
      password: 'Password123!',
      confirmPassword: 'Password123!',
    });

    component.submit();

    expect(auth.resetPassword).toHaveBeenCalledWith({ password: 'Password123!' }, 'reset-token');
  });

  it('submits a six-digit code and challenge', () => {
    const { component, auth } = create({ challenge: 'otp-challenge' });
    component.form.setValue({
      code: '123456',
      password: 'Password123!',
      confirmPassword: 'Password123!',
    });

    component.submit();

    expect(auth.resetPassword).toHaveBeenCalledWith(
      {
        password: 'Password123!',
        code: '123456',
        challenge: 'otp-challenge',
      },
      undefined,
    );
  });
});
