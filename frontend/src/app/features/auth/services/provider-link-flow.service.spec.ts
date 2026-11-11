import { Injectable, signal } from '@angular/core';
import { TestBed, fakeAsync, flushMicrotasks } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { installMemoryStorage, installThrowingStorage } from '@testing';

import {
  ApiClientClientError,
  MFA_REQUIRED_ERROR_CODE,
} from '../../../core/api/models/api-client-error.model';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import { SessionManagerService } from '../../../core/services/session-manager.service';
import { AuthReturnUrlService } from './auth-return-url.service';
import { MicrosoftTokenExchangeService } from './microsoft-token-exchange.service';
import {
  PendingEmailLinkStorageKey,
  PendingProviderLinkStorageKey,
  ProviderLinkFlowService,
} from './provider-link-flow.service';
import {
  LAST_SIGN_IN_METHOD_ERROR_CODE,
  PROVIDER_ALREADY_CONNECTED_ERROR_CODE,
  PROVIDER_ALREADY_LINKED_ERROR_CODE,
  PROVIDER_LINK_EXPIRED_ERROR_CODE,
  PROVIDER_LINK_MISMATCH_ERROR_CODE,
  ProviderLinksService,
} from './provider-links.service';

/** Records the provider redirect instead of leaving the test page. */
@Injectable()
class TestProviderLinkFlowService extends ProviderLinkFlowService {
  redirectedTo: string | null = null;

  protected override redirect(url: string): void {
    this.redirectedTo = url;
  }
}

describe('ProviderLinkFlowService', () => {
  let service: TestProviderLinkFlowService;
  let providerLinks: jasmine.SpyObj<ProviderLinksService>;
  let exchange: jasmine.SpyObj<MicrosoftTokenExchangeService>;
  let sessionLoading: ReturnType<typeof signal<boolean>>;
  let authToken: { accessToken: string | null };
  let restoreStorage: () => void;

  const pending = (overrides: Record<string, unknown> = {}) =>
    JSON.stringify({
      provider: 'google',
      state: 'state-1',
      codeVerifier: null,
      startedAt: Date.now(),
      ...overrides,
    });

  const clientError = (code: string, status = 409) =>
    new ApiClientClientError('refused', status, code);

  function setup(seed: Record<string, string> = {}): void {
    restoreStorage = installMemoryStorage('session', seed);
    providerLinks = jasmine.createSpyObj<ProviderLinksService>('ProviderLinksService', [
      'startLink',
      'completeLink',
    ]);
    exchange = jasmine.createSpyObj<MicrosoftTokenExchangeService>('MicrosoftTokenExchange', [
      'exchange',
    ]);
    sessionLoading = signal(false);
    authToken = { accessToken: 'access-token' };

    TestBed.configureTestingModule({
      providers: [
        TestProviderLinkFlowService,
        { provide: ProviderLinksService, useValue: providerLinks },
        { provide: MicrosoftTokenExchangeService, useValue: exchange },
        { provide: SessionManagerService, useValue: { loading: sessionLoading } },
        { provide: AuthTokenService, useValue: authToken },
      ],
    });
    service = TestBed.inject(TestProviderLinkFlowService);
  }

  afterEach(() => restoreStorage?.());

  describe('begin', () => {
    it('remembers the server state and sends the browser to the provider', () => {
      setup();
      providerLinks.startLink.and.returnValue(
        of({
          authorizationUrl: 'https://accounts.example/auth',
          state: 's',
          redirectUri: 'https://app.example/auth/microsoft',
          codeVerifier: 'v',
        }),
      );

      service.begin('microsoft').subscribe();

      expect(service.redirectedTo).toBe('https://accounts.example/auth');
      const stored = JSON.parse(sessionStorage.getItem(PendingProviderLinkStorageKey)!);
      expect(stored).toEqual(
        jasmine.objectContaining({
          provider: 'microsoft',
          state: 's',
          redirectUri: 'https://app.example/auth/microsoft',
          codeVerifier: 'v',
        }),
      );
    });

    it('stays on the page when the server refuses to start', () => {
      setup();
      providerLinks.startLink.and.returnValue(
        throwError(() => clientError(PROVIDER_ALREADY_CONNECTED_ERROR_CODE)),
      );
      let error: unknown;

      service.begin('google').subscribe({ error: (err) => (error = err) });

      expect(error).toBeTruthy();
      expect(service.redirectedTo).toBeNull();
      expect(sessionStorage.getItem(PendingProviderLinkStorageKey)).toBeNull();
    });
  });

  describe('isLinkCallback', () => {
    it('matches only the provider and state this browser started', () => {
      setup({ [PendingProviderLinkStorageKey]: pending() });

      expect(service.isLinkCallback('google', '?code=c&state=state-1')).toBeTrue();
      expect(service.isLinkCallback('google', '?error=access_denied&state=state-1')).toBeTrue();
      expect(service.isLinkCallback('google', '?code=c&state=other')).toBeFalse();
      expect(service.isLinkCallback('microsoft', '?code=c&state=state-1')).toBeFalse();
      expect(service.isLinkCallback('google', '?code=c')).toBeFalse();
    });

    it('treats a sign-in callback with nothing pending as a sign-in', () => {
      setup();
      expect(service.isLinkCallback('google', '?code=c&state=state-1')).toBeFalse();
    });

    it('ignores unreadable pending state', () => {
      setup({ [PendingProviderLinkStorageKey]: '{not json' });
      expect(service.isLinkCallback('google', '?code=c&state=state-1')).toBeFalse();
    });
  });

  describe('completeFromCallback', () => {
    it('sends a Google code to the server and reports success', fakeAsync(() => {
      setup({ [PendingProviderLinkStorageKey]: pending() });
      providerLinks.completeLink.and.returnValue(
        of({
          provider: 'google',
          email: null,
          displayName: null,
          linkedAtUtc: 'x',
          lastUsedAtUtc: null,
        }),
      );
      let target: string | undefined;

      service.completeFromCallback('google', '?code=c&state=state-1').then((t) => (target = t));
      flushMicrotasks();

      expect(providerLinks.completeLink).toHaveBeenCalledWith('google', {
        state: 'state-1',
        code: 'c',
      });
      expect(target).toBe('/account/security?providerLink=linked&provider=google');
      expect(sessionStorage.getItem(PendingProviderLinkStorageKey)).toBeNull();
    }));

    it('redeems a Microsoft code in the browser with the issued verifier', fakeAsync(() => {
      setup({
        [PendingProviderLinkStorageKey]: pending({
          provider: 'microsoft',
          codeVerifier: 'v',
          redirectUri: 'https://issued.example/auth/microsoft',
        }),
      });
      exchange.exchange.and.resolveTo('id-token');
      providerLinks.completeLink.and.returnValue(
        of({
          provider: 'microsoft',
          email: null,
          displayName: null,
          linkedAtUtc: 'x',
          lastUsedAtUtc: null,
        }),
      );
      let target: string | undefined;

      service.completeFromCallback('microsoft', '?code=c&state=state-1').then((t) => (target = t));
      flushMicrotasks();

      // Redeemed with exactly the redirect the server issued, not one this app rebuilt.
      expect(exchange.exchange).toHaveBeenCalledWith('c', 'v', {
        scope: 'openid profile email',
        redirectUri: 'https://issued.example/auth/microsoft',
      });
      expect(providerLinks.completeLink).toHaveBeenCalledWith('microsoft', {
        state: 'state-1',
        idToken: 'id-token',
      });
      expect(target).toContain('providerLink=linked');
    }));

    it('reports a consent the user declined as cancelled', fakeAsync(() => {
      setup({ [PendingProviderLinkStorageKey]: pending() });
      let target: string | undefined;

      service
        .completeFromCallback('google', '?error=access_denied&state=state-1')
        .then((t) => (target = t));
      flushMicrotasks();

      expect(target).toContain('providerLink=cancelled');
      expect(providerLinks.completeLink).not.toHaveBeenCalled();
    }));

    it('reports any other provider error as a failure', fakeAsync(() => {
      setup({ [PendingProviderLinkStorageKey]: pending() });
      let target: string | undefined;

      service
        .completeFromCallback('google', '?error=server_error&state=state-1')
        .then((t) => (target = t));
      flushMicrotasks();

      expect(target).toContain('providerLink=failed');
    }));

    it('fails a callback that does not match what was started', fakeAsync(() => {
      setup({ [PendingProviderLinkStorageKey]: pending() });
      let target: string | undefined;

      service.completeFromCallback('google', '?code=c&state=forged').then((t) => (target = t));
      flushMicrotasks();

      expect(target).toContain('providerLink=failed');
      expect(providerLinks.completeLink).not.toHaveBeenCalled();
    }));

    it('expires a link started longer ago than the server keeps it', fakeAsync(() => {
      setup({ [PendingProviderLinkStorageKey]: pending({ startedAt: Date.now() - 11 * 60_000 }) });
      let target: string | undefined;

      service.completeFromCallback('google', '?code=c&state=state-1').then((t) => (target = t));
      flushMicrotasks();

      expect(target).toContain('providerLink=expired');
    }));

    it('waits for the session to restore before completing', fakeAsync(() => {
      setup({ [PendingProviderLinkStorageKey]: pending() });
      sessionLoading.set(true);
      providerLinks.completeLink.and.returnValue(
        of({
          provider: 'google',
          email: null,
          displayName: null,
          linkedAtUtc: 'x',
          lastUsedAtUtc: null,
        }),
      );
      let target: string | undefined;

      service.completeFromCallback('google', '?code=c&state=state-1').then((t) => (target = t));
      flushMicrotasks();
      expect(providerLinks.completeLink).not.toHaveBeenCalled();

      sessionLoading.set(false);
      TestBed.tick();
      flushMicrotasks();

      expect(target).toContain('providerLink=linked');
    }));

    it('asks to sign in again when the session could not be restored', fakeAsync(() => {
      setup({ [PendingProviderLinkStorageKey]: pending() });
      authToken.accessToken = null;
      let target: string | undefined;

      service.completeFromCallback('google', '?code=c&state=state-1').then((t) => (target = t));
      flushMicrotasks();

      expect(target).toContain('providerLink=signed_out');
    }));

    it('fails a Microsoft return without the redirect it was issued', fakeAsync(() => {
      setup({
        [PendingProviderLinkStorageKey]: pending({ provider: 'microsoft', codeVerifier: 'v' }),
      });
      let target: string | undefined;

      service.completeFromCallback('microsoft', '?code=c&state=state-1').then((t) => (target = t));
      flushMicrotasks();

      expect(target).toContain('providerLink=failed');
      expect(exchange.exchange).not.toHaveBeenCalled();
    }));

    it('fails a Microsoft return without the verifier it needs', fakeAsync(() => {
      setup({ [PendingProviderLinkStorageKey]: pending({ provider: 'microsoft' }) });
      let target: string | undefined;

      service.completeFromCallback('microsoft', '?code=c&state=state-1').then((t) => (target = t));
      flushMicrotasks();

      expect(target).toContain('providerLink=failed');
      expect(exchange.exchange).not.toHaveBeenCalled();
    }));

    const refusals: [string, unknown, string][] = [
      ['expired', clientError(PROVIDER_LINK_EXPIRED_ERROR_CODE, 410), 'expired'],
      ['linked elsewhere', clientError(PROVIDER_ALREADY_LINKED_ERROR_CODE), 'already_linked'],
      ['provider taken', clientError(PROVIDER_ALREADY_CONNECTED_ERROR_CODE), 'already_connected'],
      ['another session', clientError(PROVIDER_LINK_MISMATCH_ERROR_CODE, 403), 'mismatch'],
      ['proof lapsed', clientError(MFA_REQUIRED_ERROR_CODE, 403), 'step_up'],
      ['token refused', new ApiClientClientError('bad', 400, 'BAD_REQUEST'), 'rejected'],
      ['anything else', clientError(LAST_SIGN_IN_METHOD_ERROR_CODE, 500), 'failed'],
      ['a thrown error', new Error('boom'), 'failed'],
    ];

    for (const [name, error, outcome] of refusals) {
      it(`maps a refusal (${name}) to its outcome`, fakeAsync(() => {
        setup({ [PendingProviderLinkStorageKey]: pending() });
        providerLinks.completeLink.and.returnValue(throwError(() => error));
        let target: string | undefined;

        service.completeFromCallback('google', '?code=c&state=state-1').then((t) => (target = t));
        flushMicrotasks();

        expect(target).toBe(`/account/security?providerLink=${outcome}&provider=google`);
      }));
    }
  });

  describe('email-match proposals', () => {
    const proposal = {
      linkToken: 'link-token',
      provider: 'google' as const,
      maskedEmail: 'o***@example.com',
      expiresAtUtc: new Date(Date.now() + 5 * 60_000).toISOString(),
    };

    it('stores, reads back, and clears a proposal', () => {
      setup();

      service.proposeEmailLink(proposal);
      expect(service.readEmailProposal()).toEqual(proposal);

      service.clearEmailProposal();
      expect(service.readEmailProposal()).toBeNull();
    });

    it('sends the next sign-in to confirm the proposal, and declining restores the original destination', () => {
      setup();
      const returnUrl = TestBed.inject(AuthReturnUrlService);
      returnUrl.set('/events/42');

      service.proposeEmailLink(proposal);
      expect(returnUrl.peek()).toBe('/account/security');

      service.declineEmailProposal();
      expect(returnUrl.peek()).toBe('/events/42');
      expect(service.readEmailProposal()).toBeNull();
    });

    it('declining leaves no destination when there was none', () => {
      setup();
      const returnUrl = TestBed.inject(AuthReturnUrlService);

      service.proposeEmailLink(proposal);
      service.declineEmailProposal();

      expect(returnUrl.peek()).toBeNull();
    });

    it('drops an expired proposal', () => {
      setup({
        [PendingEmailLinkStorageKey]: JSON.stringify({
          ...proposal,
          expiresAtUtc: new Date(Date.now() - 1000).toISOString(),
        }),
      });

      expect(service.readEmailProposal()).toBeNull();
      expect(sessionStorage.getItem(PendingEmailLinkStorageKey)).toBeNull();
    });

    it('ignores a malformed proposal', () => {
      setup({ [PendingEmailLinkStorageKey]: JSON.stringify({ linkToken: 1 }) });
      expect(service.readEmailProposal()).toBeNull();
    });

    it('works without storage, as in a private window', () => {
      setup();
      restoreStorage();
      restoreStorage = installThrowingStorage('session');

      expect(() => service.proposeEmailLink(proposal)).not.toThrow();
      expect(service.readEmailProposal()).toBeNull();
      expect(() => service.clearEmailProposal()).not.toThrow();
    });
  });
});
