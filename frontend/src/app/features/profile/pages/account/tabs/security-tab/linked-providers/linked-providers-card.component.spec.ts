import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { NEVER, of, throwError } from 'rxjs';

import { fakeActivatedRoute, flushPromises } from '@testing';

import { LinkedProvidersCardComponent, outcomeMessage } from './linked-providers-card.component';
import {
  ApiClientClientError,
  MFA_REQUIRED_ERROR_CODE,
} from '../../../../../../../core/api/models/api-client-error.model';
import { ProviderLinkFlowService } from '../../../../../../auth/services/provider-link-flow.service';
import {
  LAST_SIGN_IN_METHOD_ERROR_CODE,
  PendingEmailLinkProposal,
  PROVIDER_LINK_EXPIRED_ERROR_CODE,
  ProviderLinksOverview,
  ProviderLinksService,
} from '../../../../../../auth/services/provider-links.service';

const LINK = {
  provider: 'google' as const,
  email: 'owner@gmail.com',
  displayName: 'Owner',
  linkedAtUtc: '2026-11-01T09:00:00Z',
  lastUsedAtUtc: '2026-11-05T10:00:00Z',
};

function overview(overrides: Partial<ProviderLinksOverview> = {}): ProviderLinksOverview {
  return {
    hasPassword: true,
    providers: [
      { provider: 'google', available: true, link: LINK, canUnlink: true },
      { provider: 'microsoft', available: true, link: null, canUnlink: false },
    ],
    ...overrides,
  };
}

const PROPOSAL: PendingEmailLinkProposal = {
  linkToken: 'link-token',
  provider: 'microsoft',
  maskedEmail: 'o***@example.com',
  expiresAtUtc: '2099-01-01T00:00:00Z',
};

describe('LinkedProvidersCardComponent', () => {
  let fixture: ComponentFixture<LinkedProvidersCardComponent>;
  let component: LinkedProvidersCardComponent;
  let providerLinks: jasmine.SpyObj<ProviderLinksService>;
  let linkFlow: jasmine.SpyObj<ProviderLinkFlowService>;
  let router: jasmine.SpyObj<Router>;
  let stepUpRequired: jasmine.Spy;

  const refusal = (code: string, status = 409) => new ApiClientClientError('refused', status, code);

  async function create(queryParams: Record<string, string> = {}): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [LinkedProvidersCardComponent],
      providers: [
        { provide: ProviderLinksService, useValue: providerLinks },
        { provide: ProviderLinkFlowService, useValue: linkFlow },
        { provide: Router, useValue: router },
        { provide: ActivatedRoute, useValue: fakeActivatedRoute({ queryParams }).route },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LinkedProvidersCardComponent);
    component = fixture.componentInstance;
    stepUpRequired = jasmine.createSpy('stepUpRequired');
    component.stepUpRequired.subscribe(stepUpRequired);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function button(label: string): HTMLButtonElement {
    const match = Array.from(el().querySelectorAll<HTMLButtonElement>('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
    expect(match).withContext(`button "${label}"`).toBeDefined();
    return match!;
  }

  function banner(): string {
    return el().querySelector('[data-testid="linked-providers-banner"]')?.textContent?.trim() ?? '';
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await flushPromises();
    fixture.detectChanges();
  }

  beforeEach(() => {
    providerLinks = jasmine.createSpyObj<ProviderLinksService>('ProviderLinksService', [
      'getProviders',
      'unlink',
      'confirmLink',
    ]);
    providerLinks.getProviders.and.returnValue(of(overview()));
    linkFlow = jasmine.createSpyObj<ProviderLinkFlowService>('ProviderLinkFlowService', [
      'begin',
      'readEmailProposal',
      'clearEmailProposal',
    ]);
    linkFlow.readEmailProposal.and.returnValue(null);
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.resolveTo(true);
  });

  it('lists the password and each provider with its link details', async () => {
    await create();

    const google = el().querySelector('[data-testid="provider-google"]')!.textContent!;
    expect(google).toContain('Linked as owner@gmail.com');
    expect(google).toContain('Last used');
    expect(el().textContent).toContain('Set. Change it on the Password tab.');
    expect(button('Unlink Google').disabled).toBeFalse();
    expect(button('Link Microsoft')).toBeTruthy();
  });

  it('keeps the only sign-in method and says why', async () => {
    providerLinks.getProviders.and.returnValue(
      of(
        overview({
          hasPassword: false,
          providers: [
            { provider: 'google', available: true, link: LINK, canUnlink: false },
            { provider: 'microsoft', available: false, link: null, canUnlink: false },
          ],
        }),
      ),
    );
    await create();

    const unlink = button('Unlink Google');
    expect(unlink.disabled).toBeTrue();
    const hint = el().querySelector(`#${unlink.getAttribute('aria-describedby')}`);
    expect(hint?.textContent).toContain('only way to sign in');
    expect(el().textContent).toContain('Not available right now.');
    expect(el().textContent).toContain('Not set.');
  });

  it('starts a link through the flow', async () => {
    linkFlow.begin.and.returnValue(NEVER);
    await create();

    button('Link Microsoft').click();
    await settle();

    expect(linkFlow.begin).toHaveBeenCalledWith('microsoft');
    expect(button('Redirecting...').disabled).toBeTrue();
  });

  it('hands a missing step-up to the tab', async () => {
    linkFlow.begin.and.returnValue(throwError(() => refusal(MFA_REQUIRED_ERROR_CODE, 403)));
    await create();

    button('Link Microsoft').click();
    await settle();

    expect(stepUpRequired).toHaveBeenCalled();
    expect(button('Link Microsoft').disabled).toBeFalse();
  });

  it('shows why a link could not start', async () => {
    linkFlow.begin.and.returnValue(throwError(() => new Error('Network down')));
    await create();

    button('Link Microsoft').click();
    await settle();

    expect(banner()).toBe('Network down');
  });

  it('asks before unlinking, then unlinks and reloads', async () => {
    providerLinks.unlink.and.returnValue(of(undefined));
    await create();

    button('Unlink Google').click();
    await settle();
    expect(el().querySelector('[role="alertdialog"]')?.textContent).toContain(
      'Stop signing in with Google?',
    );

    button('Unlink').click();
    await settle();

    expect(providerLinks.unlink).toHaveBeenCalledWith('google');
    expect(providerLinks.getProviders).toHaveBeenCalledTimes(2);
    expect(banner()).toContain('Google can no longer be used to sign in.');
  });

  it('lets the user keep a link', async () => {
    await create();

    button('Unlink Google').click();
    await settle();
    button('Keep it').click();
    await settle();

    expect(el().querySelector('[role="alertdialog"]')).toBeNull();
    expect(providerLinks.unlink).not.toHaveBeenCalled();
  });

  it('explains a refusal to remove the last sign-in method', async () => {
    providerLinks.unlink.and.returnValue(throwError(() => refusal(LAST_SIGN_IN_METHOD_ERROR_CODE)));
    await create();

    button('Unlink Google').click();
    await settle();
    button('Unlink').click();
    await settle();

    expect(banner()).toContain('only way to sign in');
  });

  it('reports a link that came back from the provider, then clears the address bar', async () => {
    await create({ providerLink: 'linked', provider: 'microsoft' });

    expect(banner()).toBe('Microsoft is now linked. You can use it to sign in.');
    expect(
      el().querySelector('[data-testid="linked-providers-banner"]')?.getAttribute('role'),
    ).toBe('status');
    expect(router.navigate).toHaveBeenCalledWith(
      [],
      jasmine.objectContaining({
        queryParams: { providerLink: null, provider: null },
        replaceUrl: true,
      }),
    );
  });

  it('reports a failed return as an alert', async () => {
    await create({ providerLink: 'already_linked', provider: 'google' });

    expect(banner()).toContain('already linked to a different account');
    expect(
      el().querySelector('[data-testid="linked-providers-banner"]')?.getAttribute('role'),
    ).toBe('alert');
  });

  it('ignores an outcome it does not know', async () => {
    await create({ providerLink: 'unheard_of', provider: 'google' });

    expect(banner()).toBe('');
    expect(router.navigate).toHaveBeenCalled();
  });

  describe('email-match proposal', () => {
    beforeEach(() => linkFlow.readEmailProposal.and.returnValue(PROPOSAL));

    it('confirms the proposed link', async () => {
      providerLinks.confirmLink.and.returnValue(of({ ...LINK, provider: 'microsoft' }));
      await create();

      expect(el().querySelector('[data-testid="provider-link-proposal"]')?.textContent).toContain(
        'o***@example.com',
      );
      button('Link Microsoft').click();
      await settle();

      expect(providerLinks.confirmLink).toHaveBeenCalledWith('link-token');
      expect(linkFlow.clearEmailProposal).toHaveBeenCalled();
      expect(el().querySelector('[data-testid="provider-link-proposal"]')).toBeNull();
      expect(banner()).toContain('Microsoft is now linked');
    });

    it('keeps the proposal when only the step-up is missing', async () => {
      providerLinks.confirmLink.and.returnValue(
        throwError(() => refusal(MFA_REQUIRED_ERROR_CODE, 403)),
      );
      await create();

      button('Link Microsoft').click();
      await settle();

      expect(stepUpRequired).toHaveBeenCalled();
      expect(linkFlow.clearEmailProposal).not.toHaveBeenCalled();
    });

    it('drops a spent proposal and says so', async () => {
      providerLinks.confirmLink.and.returnValue(
        throwError(() => refusal(PROVIDER_LINK_EXPIRED_ERROR_CODE, 410)),
      );
      await create();

      button('Link Microsoft').click();
      await settle();

      expect(linkFlow.clearEmailProposal).toHaveBeenCalled();
      expect(banner()).toContain('expired');
    });

    it('can be dismissed', async () => {
      await create();

      button("Don't link").click();
      await settle();

      expect(linkFlow.clearEmailProposal).toHaveBeenCalled();
      expect(el().querySelector('[data-testid="provider-link-proposal"]')).toBeNull();
    });
  });

  it('reports a list that failed to load', async () => {
    providerLinks.getProviders.and.returnValue(throwError(() => new Error('Offline')));
    await create();

    expect(banner()).toBe('Offline');
  });

  it('has a message for every outcome', () => {
    for (const outcome of [
      'cancelled',
      'expired',
      'already_connected',
      'mismatch',
      'step_up',
      'rejected',
      'signed_out',
      'failed',
    ] as const) {
      expect(outcomeMessage(outcome, 'google')).toContain('Google');
    }
    expect(outcomeMessage('already_linked', 'microsoft')).toContain('Microsoft');
    expect(outcomeMessage('last_method', 'microsoft')).toContain('only way to sign in');
  });
});
