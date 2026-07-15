import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

import { AuthReturnUrlService } from '../../../auth/services/auth-return-url.service';
import { ClubInvitationResolve } from '../../models/club-invitation.types';
import { ClubInvitationsService } from '../../services/club-invitations.service';

@Component({
  selector: 'app-club-invite',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './club-invite.component.html',
})
export class ClubInviteComponent implements OnInit {
  token = '';
  invite: ClubInvitationResolve | null = null;
  loading = true;
  actionLoading = false;
  error = '';

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private invitations: ClubInvitationsService,
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

  accept(): void {
    if (!this.token || this.actionLoading) {
      return;
    }

    this.actionLoading = true;
    this.error = '';

    this.invitations.accept(this.token).subscribe({
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
