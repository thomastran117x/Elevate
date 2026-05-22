import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { EventInvitationResolve } from '../../models/event-invitation.types';
import { EventInvitationsService } from '../../services/event-invitations.service';
import { AuthReturnUrlService } from '../../../auth/services/auth-return-url.service';

@Component({
  selector: 'app-event-invite',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './event-invite.component.html',
  styleUrls: ['./event-invite.component.css'],
})
export class EventInviteComponent {
  token = '';
  invite: EventInvitationResolve | null = null;
  loading = true;
  actionLoading = false;
  error = '';

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private invitations: EventInvitationsService,
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

  get imageUrl(): string | null {
    return this.invite?.event?.imageUrls?.[0] ?? null;
  }

  accept(): void {
    if (!this.token || this.actionLoading) {
      return;
    }

    this.actionLoading = true;
    this.error = '';

    this.invitations.accept(this.token).subscribe({
      next: (response) => {
        this.actionLoading = false;
        if (response.invitation.eventId) {
          void this.router.navigate(['/events', response.invitation.eventId]);
          return;
        }

        this.load();
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
