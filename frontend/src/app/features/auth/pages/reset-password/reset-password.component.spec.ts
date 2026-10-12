import { convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ApiClientClientError } from '../../../../core/api/models/api-client-error.model';

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
      password: 'lantern orchard velvet compass',
      confirmPassword: 'lantern orchard velvet compass',
    });

    component.submit();

    expect(auth.resetPassword).toHaveBeenCalledWith(
      { password: 'lantern orchard velvet compass' },
      'reset-token',
    );
  });

  it('submits a six-digit code and challenge', () => {
    const { component, auth } = create({ challenge: 'otp-challenge' });
    component.form.setValue({
      code: '123456',
      password: 'lantern orchard velvet compass',
      confirmPassword: 'lantern orchard velvet compass',
    });

    component.submit();

    expect(auth.resetPassword).toHaveBeenCalledWith(
      {
        password: 'lantern orchard velvet compass',
        code: '123456',
        challenge: 'otp-challenge',
      },
      undefined,
    );
  });

  it('does not submit a password shorter than 15 characters', () => {
    const { component, auth } = create({ token: 'reset-token' });
    component.form.setValue({
      code: '',
      password: 'Sh0rt&Strong!',
      confirmPassword: 'Sh0rt&Strong!',
    });

    component.submit();

    expect(component.submitted).toBeTrue();
    expect(component.form.controls.password.hasError('passwordLength')).toBeTrue();
    expect(auth.resetPassword).not.toHaveBeenCalled();
  });

  it('does not submit mismatched passwords', () => {
    const { component, auth } = create({ token: 'reset-token' });
    component.form.setValue({
      code: '',
      password: 'lantern orchard velvet compass',
      confirmPassword: 'lantern orchard velvet compasses',
    });

    component.submit();

    expect(component.passwordsMismatch).toBeTrue();
    expect(auth.resetPassword).not.toHaveBeenCalled();
  });

  it('surfaces a server-side policy rejection', () => {
    const { component, auth } = create({ token: 'reset-token' });
    auth.resetPassword.and.returnValue(
      throwError(
        () =>
          new ApiClientClientError('Validation failed.', 400, 'VALIDATION_ERROR', {
            Password: ['Choose a less common password.'],
          }),
      ),
    );
    component.form.setValue({
      code: '',
      password: 'lantern orchard velvet compass',
      confirmPassword: 'lantern orchard velvet compass',
    });

    component.submit();

    expect(component.error).toBe('Choose a less common password.');
  });
});
