import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { flushPromises } from '@testing';

import { ActiveSessionsCardComponent } from './active-sessions-card.component';
import {
  ApiClientClientError,
  MFA_REQUIRED_ERROR_CODE,
} from '../../../../../../../core/api/models/api-client-error.model';
import { AuthTokenService } from '../../../../../../../core/api/services/auth-token.service';
import {
  AccountDevicesService,
  AccountSession,
} from '../../../../../services/account-devices.service';
import { MfaGateComponent } from '../../../mfa-gate/mfa-gate.component';

@Component({ selector: 'app-mfa-gate', standalone: true, template: '' })
class StubMfaGateComponent {
  @Input() modalOnly = false;
  @Output() verified = new EventEmitter<void>();
  @Output() dismissed = new EventEmitter<void>();
}

function session(overrides: Partial<AccountSession> = {}): AccountSession {
  return {
    id: 'other-session',
    current: false,
    transport: 'browser',
    client: 'firefox',
    deviceClass: 'mobile',
    network: '198.51.100.0/24',
    rememberMe: false,
    createdAt: '2026-11-01T09:00:00Z',
    lastSeenAt: '2026-11-10T09:00:00Z',
    expiresAt: '2026-11-11T09:00:00Z',
    ...overrides,
  };
}

const CURRENT = session({
  id: 'this-session',
  current: true,
  client: 'chrome',
  deviceClass: 'desktop',
  network: null,
});
const OTHER = session();

describe('ActiveSessionsCardComponent', () => {
  let fixture: ComponentFixture<ActiveSessionsCardComponent>;
  let devices: jasmine.SpyObj<AccountDevicesService>;
  let router: jasmine.SpyObj<Router>;
  let authToken: jasmine.SpyObj<AuthTokenService>;

  const mfaRequired = () => new ApiClientClientError('Verify again', 403, MFA_REQUIRED_ERROR_CODE);

  async function create(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [ActiveSessionsCardComponent],
      providers: [
        { provide: AccountDevicesService, useValue: devices },
        { provide: Router, useValue: router },
        { provide: AuthTokenService, useValue: authToken },
      ],
    })
      .overrideComponent(ActiveSessionsCardComponent, {
        remove: { imports: [MfaGateComponent] },
        add: { imports: [StubMfaGateComponent] },
      })
      .compileComponents();

    fixture = TestBed.createComponent(ActiveSessionsCardComponent);
    fixture.detectChanges();
    await settle();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function row(id: string): HTMLElement {
    const match = el().querySelector<HTMLElement>(`[data-testid="session-${id}"]`);
    expect(match).withContext(`session ${id}`).not.toBeNull();
    return match!;
  }

  function signOutButton(id: string): HTMLButtonElement {
    return row(id).querySelector('button')!;
  }

  function button(label: string, root: ParentNode = el()): HTMLButtonElement {
    const match = Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
    expect(match).withContext(`button "${label}"`).toBeDefined();
    return match!;
  }

  function dialog(): HTMLElement | null {
    return el().querySelector('[role="dialog"]');
  }

  function banner(): string {
    return el().querySelector('[data-testid="sessions-banner"]')?.textContent?.trim() ?? '';
  }

  function gate(): StubMfaGateComponent | null {
    return (
      fixture.debugElement.query(By.directive(StubMfaGateComponent))?.componentInstance ?? null
    );
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await flushPromises();
    fixture.detectChanges();
  }

  async function confirm(label: string): Promise<void> {
    await settle();
    button(label, dialog()!).click();
    await settle();
  }

  beforeEach(() => {
    devices = jasmine.createSpyObj<AccountDevicesService>('AccountDevicesService', [
      'listSessions',
      'revokeSession',
      'revokeOtherSessions',
      'revokeAllSessions',
    ]);
    devices.listSessions.and.returnValue(of([CURRENT, OTHER]));
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.resolveTo(true);
    authToken = jasmine.createSpyObj<AuthTokenService>('AuthTokenService', ['logoutLocal']);
  });

  it('lists each session, marking this device', async () => {
    devices.listSessions.and.returnValue(of([CURRENT, session({ id: 'api', transport: 'api' })]));
    await create();

    expect(row('this-session').textContent).toContain('Chrome · Desktop');
    expect(row('this-session').textContent).toContain('This device');
    expect(row('api').textContent).toContain('Firefox · Phone');
    expect(row('api').textContent).toContain('API access');
    expect(row('api').textContent).toContain('from 198.51.100.0/24');
    expect(row('api').textContent).not.toContain('This device');
    expect(signOutButton('this-session').getAttribute('aria-label')).toBe(
      'Sign out of this device',
    );
    expect(signOutButton('api').getAttribute('aria-label')).toBe(
      'Sign out Firefox · Phone session',
    );
  });

  it('offers to retry when the list cannot load', async () => {
    devices.listSessions.and.returnValue(throwError(() => new Error('Network down')));
    await create();

    expect(el().querySelector('[role="alert"]')?.textContent).toContain('Network down');

    devices.listSessions.and.returnValue(of([CURRENT]));
    button('Try again').click();
    await settle();

    expect(row('this-session')).toBeTruthy();
  });

  it('cannot sign out others when this is the only session', async () => {
    devices.listSessions.and.returnValue(of([CURRENT]));
    await create();

    expect(button('Sign out other sessions').disabled).toBeTrue();
    expect(button('Sign out everywhere').disabled).toBeFalse();
  });

  it('asks before signing out another session, then reloads the list', async () => {
    devices.revokeSession.and.returnValue(of({ revokedCount: 1, currentSessionRevoked: false }));
    await create();

    signOutButton('other-session').click();
    await settle();
    expect(dialog()?.textContent).toContain('Sign out Firefox · Phone?');
    expect(dialog()?.textContent).toContain('This device stays signed in.');
    devices.listSessions.and.returnValue(of([CURRENT]));
    await confirm('Sign out');

    expect(devices.revokeSession).toHaveBeenCalledWith('other-session');
    expect(banner()).toBe('Firefox · Phone was signed out.');
    expect(el().querySelector('[data-testid="session-other-session"]')).toBeNull();
    expect(dialog()).toBeNull();
    expect(authToken.logoutLocal).not.toHaveBeenCalled();
  });

  it('does nothing when the confirmation is cancelled', async () => {
    await create();

    signOutButton('other-session').click();
    await confirm('Keep signed in');

    expect(devices.revokeSession).not.toHaveBeenCalled();
    expect(dialog()).toBeNull();
  });

  it('signs this browser out when its own session is ended', async () => {
    devices.revokeSession.and.returnValue(of({ revokedCount: 1, currentSessionRevoked: true }));
    await create();

    signOutButton('this-session').click();
    await settle();
    expect(dialog()?.textContent).toContain('Sign out of this device?');
    await confirm('Sign out');

    expect(devices.revokeSession).toHaveBeenCalledWith('this-session');
    expect(authToken.logoutLocal).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/auth/login']);
  });

  it('signs out every other session and keeps this one', async () => {
    devices.revokeOtherSessions.and.returnValue(
      of({ revokedCount: 2, currentSessionRevoked: false }),
    );
    await create();

    button('Sign out other sessions').click();
    await confirm('Sign out others');

    expect(devices.revokeOtherSessions).toHaveBeenCalled();
    expect(banner()).toBe('2 other sessions were signed out.');
    expect(authToken.logoutLocal).not.toHaveBeenCalled();
  });

  it('signs out everywhere, this browser included', async () => {
    devices.revokeAllSessions.and.returnValue(of({ revokedCount: 2, currentSessionRevoked: true }));
    await create();

    button('Sign out everywhere').click();
    await settle();
    expect(dialog()?.textContent).toContain('including this one');
    await confirm('Sign out everywhere');

    expect(authToken.logoutLocal).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/auth/login']);
  });

  it('verifies and then finishes a sign-out refused for a missing step-up', async () => {
    devices.revokeOtherSessions.and.returnValues(
      throwError(() => mfaRequired()),
      of({ revokedCount: 1, currentSessionRevoked: false }),
    );
    await create();

    button('Sign out other sessions').click();
    await confirm('Sign out others');
    expect(gate()?.modalOnly).toBeTrue();
    expect(dialog()).toBeNull();

    gate()!.verified.emit();
    await settle();

    expect(devices.revokeOtherSessions).toHaveBeenCalledTimes(2);
    expect(gate()).toBeNull();
    expect(banner()).toBe('One other session was signed out.');
  });

  it('drops the sign-out when verification is dismissed', async () => {
    devices.revokeOtherSessions.and.returnValue(throwError(() => mfaRequired()));
    await create();

    button('Sign out other sessions').click();
    await confirm('Sign out others');
    gate()!.dismissed.emit();
    await settle();

    expect(gate()).toBeNull();
    expect(devices.revokeOtherSessions).toHaveBeenCalledTimes(1);
  });

  it('reports any other failure and refreshes the list', async () => {
    devices.revokeSession.and.returnValue(
      throwError(() => new ApiClientClientError('Session not found.', 404, 'NOT_FOUND')),
    );
    await create();

    signOutButton('other-session').click();
    await confirm('Sign out');

    expect(banner()).toBe('Session not found.');
    expect(devices.listSessions).toHaveBeenCalledTimes(2);
  });

  it('says so when there was nothing else to sign out', async () => {
    devices.revokeOtherSessions.and.returnValue(
      of({ revokedCount: 0, currentSessionRevoked: false }),
    );
    await create();

    button('Sign out other sessions').click();
    await confirm('Sign out others');

    expect(banner()).toBe('There were no other sessions to sign out.');
  });
});
