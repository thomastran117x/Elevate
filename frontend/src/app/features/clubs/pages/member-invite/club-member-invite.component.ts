import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

import { AuthReturnUrlService } from '../../../auth/services/auth-return-url.service';
import { ClubMemberInvitationResolve } from '../../models/club-member-invitation.types';
import { ClubMemberInvitationsService } from '../../services/club-member-invitations.service';

@Component({
  selector: 'app-club-member-invite',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './club-member-invite.component.html',
})
export class ClubMemberInviteComponent implements OnInit {
  token = '';
  invite: ClubMemberInvitationResolve | null = null;
  loading = true;
  actionLoading = false;
  error = '';

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private invitations: ClubMemberInvitationsService,
    private authReturnUrl: AuthReturnUrlService,
  ) {}

  ngOnInit(): void {
    this.token = this.route.snapshot.queryParamMap.get('token') ?? '';
    if (!this.token) {
      this.loading = false;
      this.error = 'This invitation link is missing its token.';
      return;
    }

    this.load();
  }

  /** Accept a specific invite, or redeem a shared link — the resolved source decides which. */
  accept(): void {
    if (!this.token || this.actionLoading || !this.invite) {
      return;
    }

    this.actionLoading = true;
    this.error = '';

    const request$ =
      this.invite.source === 'Link'
        ? this.invitations.redeemLink(this.token)
        : this.invitations.accept(this.token);

    request$.subscribe({
      next: (decision) => {
        this.actionLoading = false;
        void this.router.navigate(['/clubs', decision.clubId]);
      },
      error: (err) => {
        this.actionLoading = false;
        this.error = err?.error?.message || 'We could not accept this invitation.';
        this.load();
      },
    });
  }

  decline(): void {
    if (!this.token || this.actionLoading) {
      return;
    }

    this.actionLoading = true;
    this.error = '';

    this.invitations.decline(this.token).subscribe({
      next: () => {
        this.actionLoading = false;
        this.load();
      },
      error: (err) => {
        this.actionLoading = false;
        this.error = err?.error?.message || 'We could not decline this invitation.';
        this.load();
      },
    });
  }

  goToClub(): void {
    if (this.invite?.club?.id) {
      void this.router.navigate(['/clubs', this.invite.club.id]);
    }
  }

  goToAuth(path: 'login' | 'signup'): void {
    this.authReturnUrl.set(this.router.url);
    void this.router.navigate(['/auth', path], {
      queryParams: {
        returnUrl: this.router.url,
      },
    });
  }

  private load(): void {
    this.loading = true;
    this.error = '';

    this.invitations.resolve(this.token).subscribe({
      next: (invite) => {
        this.invite = invite;
        this.loading = false;
      },
      error: (err) => {
        this.loading = false;
        this.error = err?.error?.message || 'We could not load this invitation.';
      },
    });
  }
}
