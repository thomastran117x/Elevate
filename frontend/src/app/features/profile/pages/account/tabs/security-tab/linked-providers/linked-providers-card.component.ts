import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  computed,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs/operators';

import {
  getApiClientMessage,
  isApiClientErrorCode,
  MFA_REQUIRED_ERROR_CODE,
} from '../../../../../../../core/api/models/api-client-error.model';
import { AuthTokenService } from '../../../../../../../core/api/services/auth-token.service';
import {
  PROVIDER_LINK_OUTCOMES,
  PROVIDER_LINK_PROVIDER_PARAM,
  PROVIDER_LINK_RESULT_PARAM,
  ProviderLinkFlowService,
  ProviderLinkOutcome,
} from '../../../../../../auth/services/provider-link-flow.service';
import {
  isLinkableProvider,
  LAST_SIGN_IN_METHOD_ERROR_CODE,
  LinkableProvider,
  PendingEmailLinkProposal,
  PROVIDER_ALREADY_CONNECTED_ERROR_CODE,
  PROVIDER_ALREADY_LINKED_ERROR_CODE,
  PROVIDER_LABELS,
  PROVIDER_LINK_EXPIRED_ERROR_CODE,
  PROVIDER_LINK_MISMATCH_ERROR_CODE,
  ProviderLinksOverview,
  ProviderLinksService,
  ProviderLinkStatus,
} from '../../../../../../auth/services/provider-links.service';

interface Banner {
  tone: 'success' | 'error';
  text: string;
}

/**
 * The account's sign-in providers: which are linked, and linking or unlinking one. It lives inside
 * the security tab's step-up gate. Every change spends the step-up proof, so a refusal for a
 * missing proof is handed to the tab, which re-locks and prompts as for any other factor.
 *
 * It also finishes the two flows that leave the page: the result of a link that went to the
 * provider and back (read from the query string), and a link a provider sign-in proposed because
 * its email matched this account (read from session storage).
 */
@Component({
  selector: 'app-linked-providers-card',
  standalone: true,
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './linked-providers-card.component.html',
})
export class LinkedProvidersCardComponent implements OnInit {
  /** Emitted when a change was refused because the session's step-up proof is missing. */
  readonly stepUpRequired = output<void>();

  readonly labels = PROVIDER_LABELS;
  readonly overview = signal<ProviderLinksOverview | null>(null);
  readonly loading = signal(true);
  readonly busyProvider = signal<LinkableProvider | null>(null);
  readonly confirmingUnlink = signal<LinkableProvider | null>(null);
  readonly proposal = signal<PendingEmailLinkProposal | null>(null);
  readonly confirmingProposal = signal(false);
  readonly banner = signal<Banner | null>(null);

  readonly providers = computed<ProviderLinkStatus[]>(() => this.overview()?.providers ?? []);

  private readonly bannerRef = viewChild<ElementRef<HTMLElement>>('bannerRef');
  private readonly providerLinks = inject(ProviderLinksService);
  private readonly linkFlow = inject(ProviderLinkFlowService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly authToken = inject(AuthTokenService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);

  ngOnInit(): void {
    this.proposal.set(this.linkFlow.readEmailProposal());
    this.showReturnedOutcome();
    this.load();
  }

  link(provider: LinkableProvider): void {
    if (this.busyProvider()) {
      return;
    }

    this.banner.set(null);
    this.busyProvider.set(provider);
    // On success the browser leaves for the provider, so only a refusal comes back here.
    this.linkFlow
      .begin(provider)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: (err) => {
          this.busyProvider.set(null);
          this.handleError(err, provider, `Unable to start linking ${this.labels[provider]}.`);
        },
      });
  }

  requestUnlink(provider: LinkableProvider): void {
    this.banner.set(null);
    this.confirmingUnlink.set(provider);
  }

  cancelUnlink(): void {
    this.confirmingUnlink.set(null);
  }

  unlink(provider: LinkableProvider): void {
    if (this.busyProvider()) {
      return;
    }

    this.busyProvider.set(provider);
    this.providerLinks
      .unlink(provider)
      .pipe(
        finalize(() => this.busyProvider.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        // Unlinking ends every session, this one included, so nothing the removed provider
        // signed in keeps access. Sign in again, as after a password change.
        next: () => {
          this.confirmingUnlink.set(null);
          this.authToken.logoutLocal();
          void this.router.navigate(['/auth/login']);
        },
        error: (err) => {
          this.confirmingUnlink.set(null);
          this.handleError(err, provider, `Unable to unlink ${this.labels[provider]}.`);
        },
      });
  }

  confirmProposal(): void {
    const proposal = this.proposal();
    if (!proposal || this.confirmingProposal()) {
      return;
    }

    this.banner.set(null);
    this.confirmingProposal.set(true);
    this.providerLinks
      .confirmLink(proposal.linkToken)
      .pipe(
        finalize(() => this.confirmingProposal.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.dropProposal();
          this.show('success', `${this.labels[proposal.provider]} is now linked to your account.`);
          this.load();
        },
        error: (err) => {
          // Only a missing step-up leaves the proposal usable; anything else spent or refused it.
          if (!isApiClientErrorCode(err, MFA_REQUIRED_ERROR_CODE)) {
            this.dropProposal();
          }
          this.handleError(
            err,
            proposal.provider,
            `Unable to link ${this.labels[proposal.provider]}.`,
          );
        },
      });
  }

  dismissProposal(): void {
    this.dropProposal();
  }

  private load(): void {
    this.loading.set(true);
    this.providerLinks
      .getProviders()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (overview) => this.overview.set(overview),
        error: (err) =>
          this.show('error', getApiClientMessage(err, 'Unable to load your sign-in providers.')),
      });
  }

  /** Reports how a link that went to the provider ended, then drops it from the address bar. */
  private showReturnedOutcome(): void {
    const params = this.route.snapshot.queryParamMap;
    const outcome = params.get(PROVIDER_LINK_RESULT_PARAM);
    const provider = params.get(PROVIDER_LINK_PROVIDER_PARAM);
    if (!outcome || !isLinkableProvider(provider)) {
      return;
    }

    if ((PROVIDER_LINK_OUTCOMES as readonly string[]).includes(outcome)) {
      const result = outcome as ProviderLinkOutcome;
      this.show(result === 'linked' ? 'success' : 'error', outcomeMessage(result, provider));
    }

    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { [PROVIDER_LINK_RESULT_PARAM]: null, [PROVIDER_LINK_PROVIDER_PARAM]: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  private handleError(err: unknown, provider: LinkableProvider, fallback: string): void {
    if (isApiClientErrorCode(err, MFA_REQUIRED_ERROR_CODE)) {
      this.stepUpRequired.emit();
      return;
    }

    const code = errorOutcome(err);
    this.show('error', code ? outcomeMessage(code, provider) : getApiClientMessage(err, fallback));
  }

  private dropProposal(): void {
    this.linkFlow.clearEmailProposal();
    this.proposal.set(null);
  }

  /** Screen reader and keyboard users land on the result rather than on a button that moved. */
  private show(tone: Banner['tone'], text: string): void {
    this.banner.set({ tone, text });
    afterNextRender(() => this.bannerRef()?.nativeElement.focus(), { injector: this.injector });
  }
}

function errorOutcome(err: unknown): ProviderLinkOutcome | 'last_method' | null {
  if (isApiClientErrorCode(err, LAST_SIGN_IN_METHOD_ERROR_CODE)) return 'last_method';
  if (isApiClientErrorCode(err, PROVIDER_LINK_EXPIRED_ERROR_CODE)) return 'expired';
  if (isApiClientErrorCode(err, PROVIDER_ALREADY_LINKED_ERROR_CODE)) return 'already_linked';
  if (isApiClientErrorCode(err, PROVIDER_ALREADY_CONNECTED_ERROR_CODE)) return 'already_connected';
  if (isApiClientErrorCode(err, PROVIDER_LINK_MISMATCH_ERROR_CODE)) return 'mismatch';
  return null;
}

export function outcomeMessage(
  outcome: ProviderLinkOutcome | 'last_method',
  provider: LinkableProvider,
): string {
  const name = PROVIDER_LABELS[provider];
  switch (outcome) {
    case 'linked':
      return `${name} is now linked. You can use it to sign in.`;
    case 'cancelled':
      return `Linking ${name} was cancelled. Nothing was changed.`;
    case 'expired':
      return `That ${name} link request expired. Start again.`;
    case 'already_linked':
      return `That ${name} account is already linked to a different account.`;
    case 'already_connected':
      return `Your account already links a different ${name} account. Unlink it first.`;
    case 'mismatch':
      return `That ${name} link request belongs to a different account or session.`;
    case 'step_up':
      return `Your verification expired while linking ${name}. Verify again, then start over.`;
    case 'rejected':
      return `${name} did not confirm the account. Try again.`;
    case 'signed_out':
      return `Sign in again, then link ${name} from this page.`;
    case 'last_method':
      return `${name} is your only way to sign in. Set a password, link another provider, or add a passkey first.`;
    default:
      return `Linking ${name} failed. Try again.`;
  }
}
