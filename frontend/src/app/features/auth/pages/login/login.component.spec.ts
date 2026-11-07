import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { throwError } from 'rxjs';

import { ApiClientClientError } from '../../../../core/api/models/api-client-error.model';
import { SessionManagerService } from '../../../../core/services/session-manager.service';
import { AuthReturnUrlService } from '../../services/auth-return-url.service';
import { AuthService } from '../../services/auth.service';
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

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    passwordInput = fixture.nativeElement.querySelector(
      'input[formControlName="password"]',
    ) as HTMLInputElement;
  }

  beforeEach(async () => {
    proposal = null;
    linkFlow = jasmine.createSpyObj<ProviderLinkFlowService>('ProviderLinkFlowService', [
      'readEmailProposal',
      'clearEmailProposal',
    ]);
    linkFlow.readEmailProposal.and.callFake(() => proposal);
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['login']);
    recaptcha = jasmine.createSpyObj<RecaptchaV3Service>('RecaptchaV3Service', ['execute']);
    recaptcha.execute.and.resolveTo('captcha-token');

    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: auth,
        },
        {
          provide: SessionManagerService,
          useValue: jasmine.createSpyObj<SessionManagerService>('SessionManagerService', [
            'bootstrapSession',
          ]),
        },
        {
          provide: RecaptchaV3Service,
          useValue: recaptcha,
        },
        {
          provide: AuthReturnUrlService,
          useValue: jasmine.createSpyObj<AuthReturnUrlService>('AuthReturnUrlService', [
            'captureFromRoute',
            'peek',
            'consume',
          ]),
        },
        { provide: ProviderLinkFlowService, useValue: linkFlow },
      ],
    }).compileComponents();

    await render();
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

    expect(linkFlow.clearEmailProposal).toHaveBeenCalled();
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
