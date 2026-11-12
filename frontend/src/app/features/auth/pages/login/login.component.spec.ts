import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { flushPromises, provideFeatureFlags } from '@testing';

import { ApiClientClientError } from '../../../../core/api/models/api-client-error.model';
import { AuthenticatedSessionResponse } from '../../../../core/models/auth-response.model';
import { SessionManagerService } from '../../../../core/services/session-manager.service';
import { AuthReturnUrlService } from '../../services/auth-return-url.service';
import { AuthService } from '../../services/auth.service';
import { PasskeyOutcome, PasskeysService } from '../../services/passkeys.service';
import { ProviderLinkFlowService } from '../../services/provider-link-flow.service';
import { PendingEmailLinkProposal } from '../../services/provider-links.service';
import { RecaptchaV3Service } from '../../services/recaptcha.service';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let passwordInput: HTMLInputElement;
  let auth: jasmine.SpyObj<AuthService>;
  let recaptcha: jasmine.SpyObj<RecaptchaV3Service>;
  let linkFlow: jasmine.SpyObj<ProviderLinkFlowService>;
  let proposal: PendingEmailLinkProposal | null;
  let passkeys: jasmine.SpyObj<PasskeysService>;
  let sessionManager: jasmine.SpyObj<SessionManagerService>;
  let returnUrl: jasmine.SpyObj<AuthReturnUrlService>;

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    // afterNextRender runs once the first render settles.
    await fixture.whenStable();
    fixture.detectChanges();
    passwordInput = fixture.nativeElement.querySelector(
      'input[formControlName="password"]',
    ) as HTMLInputElement;
  }

  beforeEach(async () => {
    proposal = null;
    linkFlow = jasmine.createSpyObj<ProviderLinkFlowService>('ProviderLinkFlowService', [
      'readEmailProposal',
      'declineEmailProposal',
    ]);
    linkFlow.readEmailProposal.and.callFake(() => proposal);
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['login']);
    recaptcha = jasmine.createSpyObj<RecaptchaV3Service>('RecaptchaV3Service', ['execute']);
    recaptcha.execute.and.resolveTo('captcha-token');
    passkeys = jasmine.createSpyObj<PasskeysService>('PasskeysService', [
      'isSupported',
      'isConditionalMediationAvailable',
      'signIn',
    ]);
    passkeys.isSupported.and.returnValue(false);
    passkeys.isConditionalMediationAvailable.and.resolveTo(false);
    sessionManager = jasmine.createSpyObj<SessionManagerService>('SessionManagerService', [
      'bootstrapSession',
    ]);
    sessionManager.bootstrapSession.and.resolveTo();
    returnUrl = jasmine.createSpyObj<AuthReturnUrlService>('AuthReturnUrlService', [
      'captureFromRoute',
      'peek',
      'consume',
    ]);
    returnUrl.consume.and.callFake((fallback: string) => fallback);

    configure();
    await render();
  });

  function configure(flags: Record<string, boolean> = {}): void {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        provideFeatureFlags(flags),
        {
          provide: AuthService,
          useValue: auth,
        },
        { provide: SessionManagerService, useValue: sessionManager },
        {
          provide: RecaptchaV3Service,
          useValue: recaptcha,
        },
        { provide: AuthReturnUrlService, useValue: returnUrl },
        { provide: ProviderLinkFlowService, useValue: linkFlow },
        { provide: PasskeysService, useValue: passkeys },
      ],
    });
  }

  function passkeyButton(): HTMLButtonElement | null {
    return fixture.nativeElement.querySelector('[data-testid="passkey-sign-in"]');
  }

  const signedIn: AuthenticatedSessionResponse = {
    AccessToken: 'access',
    ExpiresAtUtc: '2099-01-01T00:00:00Z',
  } as AuthenticatedSessionResponse;

  describe('passkey sign-in', () => {
    beforeEach(() => {
      passkeys.isSupported.and.returnValue(true);
    });

    it('offers no passkey button where the browser cannot use passkeys', async () => {
      passkeys.isSupported.and.returnValue(false);
      await render();

      expect(passkeyButton()).toBeNull();
    });

    it('offers no passkey button when the feature is off', async () => {
      configure({ 'auth.passkeys': false });
      await render();

      expect(passkeyButton()).toBeNull();
    });

    it('signs in with a passkey and continues to the saved destination', async () => {
      passkeys.signIn.and.returnValue(of({ status: 'done', value: signedIn }));
      const router = TestBed.inject(Router);
      const navigate = spyOn(router, 'navigateByUrl').and.resolveTo(true);
      await render();
      fixture.componentInstance.form.patchValue({ rememberMe: true });

      passkeyButton()!.click();
      await flushPromises();

      expect(passkeys.signIn).toHaveBeenCalledWith(true);
      expect(sessionManager.bootstrapSession).toHaveBeenCalledWith(signedIn);
      expect(returnUrl.consume).toHaveBeenCalledWith('/dashboard');
      expect(navigate).toHaveBeenCalledWith('/dashboard');
    });

    const outcomes: [PasskeyOutcome<AuthenticatedSessionResponse>['status'], string][] = [
      ['cancelled', 'was cancelled'],
      ['unsupported', "can't use passkeys"],
      ['failed', 'Passkey sign-in failed'],
    ];
    for (const [status, message] of outcomes) {
      it(`explains a ${status} passkey prompt and keeps the password form`, async () => {
        passkeys.signIn.and.returnValue(
          of({ status }) as Observable<PasskeyOutcome<AuthenticatedSessionResponse>>,
        );
        await render();

        passkeyButton()!.click();
        await flushPromises();
        fixture.detectChanges();

        expect(fixture.nativeElement.textContent).toContain(message);
        expect(sessionManager.bootstrapSession).not.toHaveBeenCalled();
        expect(
          fixture.nativeElement.querySelector('input[formControlName="password"]'),
        ).not.toBeNull();
      });
    }

    it('shows the generic refusal when the server rejects the passkey', async () => {
      passkeys.signIn.and.returnValue(
        throwError(
          () =>
            new ApiClientClientError(
              'Passkey verification failed. Please try again.',
              401,
              'AUTHENTICATION_FAILED',
            ),
        ),
      );
      await render();

      passkeyButton()!.click();
      await flushPromises();
      fixture.detectChanges();

      const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
      expect(alert.textContent).toContain('Passkey verification failed.');
    });

    it('offers passkeys in autofill, and withdraws the offer when the password form is used', async () => {
      passkeys.isConditionalMediationAvailable.and.resolveTo(true);
      let signal: AbortSignal | undefined;
      passkeys.signIn.and.callFake((_remember, options) => {
        signal = options?.signal;
        return new Observable<PasskeyOutcome<AuthenticatedSessionResponse>>();
      });
      auth.login.and.returnValue(throwError(() => new Error('stop')));
      await render();
      await flushPromises();

      expect(passkeys.signIn).toHaveBeenCalledWith(false, {
        mediation: 'conditional',
        signal: jasmine.any(AbortSignal),
      });
      expect(signal!.aborted).toBeFalse();
      const username = fixture.nativeElement.querySelector(
        'input[formControlName="username"]',
      ) as HTMLInputElement;
      expect(username.getAttribute('autocomplete')).toBe('username webauthn');

      fixture.componentInstance.form.patchValue({ username: 'member', password: 'Password123!' });
      await fixture.componentInstance.onSubmit();

      expect(signal!.aborted).toBeTrue();
    });

    it('signs in when a passkey is picked from autofill', async () => {
      passkeys.isConditionalMediationAvailable.and.resolveTo(true);
      passkeys.signIn.and.returnValue(of({ status: 'done', value: signedIn }));
      spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
      await render();
      await flushPromises();

      expect(sessionManager.bootstrapSession).toHaveBeenCalledWith(signedIn);
    });

    it('stays quiet when an autofill offer ends without a choice', async () => {
      passkeys.isConditionalMediationAvailable.and.resolveTo(true);
      passkeys.signIn.and.returnValue(
        of({ status: 'cancelled' }) as Observable<PasskeyOutcome<AuthenticatedSessionResponse>>,
      );
      await render();
      await flushPromises();
      fixture.detectChanges();

      expect(fixture.componentInstance.notice).toBe('');
      expect(fixture.componentInstance.error).toBe('');
    });

    it('aborts the autofill offer when the page goes away', async () => {
      passkeys.isConditionalMediationAvailable.and.resolveTo(true);
      let signal: AbortSignal | undefined;
      passkeys.signIn.and.callFake((_remember, options) => {
        signal = options?.signal;
        return new Observable<PasskeyOutcome<AuthenticatedSessionResponse>>();
      });
      await render();
      await flushPromises();

      fixture.destroy();

      expect(signal!.aborted).toBeTrue();
    });
  });

  it('shows no link proposal on an ordinary visit', () => {
    expect(
      fixture.nativeElement.querySelector('[data-testid="provider-link-proposal"]'),
    ).toBeNull();
  });

  it('explains a proposed provider link, and lets the user decline it', async () => {
    proposal = {
      linkToken: 'link-token',
      provider: 'google',
      maskedEmail: 'o***@example.com',
      expiresAtUtc: '2099-01-01T00:00:00Z',
    };
    await render();

    const banner = fixture.nativeElement.querySelector(
      '[data-testid="provider-link-proposal"]',
    ) as HTMLElement;
    expect(banner.textContent).toContain('o***@example.com');
    expect(banner.textContent).toContain('confirm linking Google');

    (banner.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(linkFlow.declineEmailProposal).toHaveBeenCalled();
    expect(
      fixture.nativeElement.querySelector('[data-testid="provider-link-proposal"]'),
    ).toBeNull();
  });

  it('hides the caps lock warning initially', () => {
    expect(getCapsLockWarning()).toBeNull();
    expect(passwordInput.getAttribute('aria-describedby')).toBeNull();
  });

  it('shows and announces the warning while caps lock is on, then hides it when off', () => {
    passwordInput.dispatchEvent(keyboardEventWithCapsLock('keydown', true));
    fixture.detectChanges();

    const warning = getCapsLockWarning();
    expect(warning?.textContent).toContain('Caps Lock is on');
    expect(warning?.getAttribute('role')).toBe('status');
    expect(warning?.getAttribute('aria-live')).toBe('polite');
    expect(passwordInput.getAttribute('aria-describedby')).toBe('login-caps-lock-warning');

    passwordInput.dispatchEvent(keyboardEventWithCapsLock('keyup', false));
    fixture.detectChanges();

    expect(getCapsLockWarning()).toBeNull();
    expect(passwordInput.getAttribute('aria-describedby')).toBeNull();
  });

  it('clears the caps lock warning when the password field loses focus', () => {
    passwordInput.dispatchEvent(keyboardEventWithCapsLock('keyup', true));
    fixture.detectChanges();
    expect(getCapsLockWarning()).not.toBeNull();

    passwordInput.dispatchEvent(new FocusEvent('blur'));
    fixture.detectChanges();

    expect(getCapsLockWarning()).toBeNull();
    expect(passwordInput.getAttribute('aria-describedby')).toBeNull();
  });

  it('omits captcha initially and obtains it only after server escalation', async () => {
    auth.login.and.returnValue(
      throwError(
        () =>
          new ApiClientClientError(
            'Authentication failed.',
            401,
            'AUTHENTICATION_FAILED',
            undefined,
            undefined,
            { captchaRequired: true },
          ),
      ),
    );
    fixture.componentInstance.form.patchValue({
      username: 'Member',
      password: 'Password123!',
    });

    await fixture.componentInstance.onSubmit();

    expect(recaptcha.execute).not.toHaveBeenCalled();
    expect(auth.login.calls.mostRecent().args[0].captcha).toBeUndefined();
    expect(fixture.componentInstance.captchaRequired).toBeTrue();

    await fixture.componentInstance.onSubmit();

    expect(recaptcha.execute).toHaveBeenCalledWith(jasmine.any(String), 'login');
    expect(auth.login.calls.mostRecent().args[0].captcha).toBe('captcha-token');
  });

  function getCapsLockWarning(): HTMLElement | null {
    return fixture.nativeElement.querySelector('#login-caps-lock-warning') as HTMLElement | null;
  }

  function keyboardEventWithCapsLock(type: 'keydown' | 'keyup', enabled: boolean): KeyboardEvent {
    const event = new KeyboardEvent(type, { bubbles: true, key: 'a' });
    Object.defineProperty(event, 'getModifierState', {
      value: (key: string) => key === 'CapsLock' && enabled,
    });
    return event;
  }
});
