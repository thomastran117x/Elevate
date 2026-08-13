import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { SessionManagerService } from '../../../../core/services/session-manager.service';
import { AuthReturnUrlService } from '../../services/auth-return-url.service';
import { AuthService } from '../../services/auth.service';
import { RecaptchaV3Service } from '../../services/recaptcha.service';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let passwordInput: HTMLInputElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: jasmine.createSpyObj<AuthService>('AuthService', ['login']),
        },
        {
          provide: SessionManagerService,
          useValue: jasmine.createSpyObj<SessionManagerService>('SessionManagerService', [
            'bootstrapSession',
          ]),
        },
        {
          provide: RecaptchaV3Service,
          useValue: jasmine.createSpyObj<RecaptchaV3Service>('RecaptchaV3Service', ['execute']),
        },
        {
          provide: AuthReturnUrlService,
          useValue: jasmine.createSpyObj<AuthReturnUrlService>('AuthReturnUrlService', [
            'captureFromRoute',
            'peek',
            'consume',
          ]),
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    passwordInput = fixture.nativeElement.querySelector(
      'input[formControlName="password"]',
    ) as HTMLInputElement;
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
