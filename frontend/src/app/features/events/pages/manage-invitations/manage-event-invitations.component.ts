import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { EventInvitation, EventInvitationLink } from '../../models/event-invitation.types';
import { EventInvitationsService } from '../../services/event-invitations.service';

@Component({
  selector: 'app-manage-event-invitations',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './manage-event-invitations.component.html',
  styleUrls: ['./manage-event-invitations.component.css'],
})
export class ManageEventInvitationsComponent {
  private readonly fb = new FormBuilder();

  eventId = 0;
  invitations: EventInvitation[] = [];
  links: EventInvitationLink[] = [];
  loading = true;
  error = '';
  latestShareUrl = '';

  readonly inviteForm = this.fb.nonNullable.group({
    emails: this.fb.nonNullable.control(''),
    userIds: this.fb.nonNullable.control(''),
    expiresAt: this.fb.nonNullable.control(''),
  });

  readonly linkForm = this.fb.nonNullable.group({
    maxRedemptions: this.fb.nonNullable.control(1, [Validators.min(1)]),
    expiresAt: this.fb.nonNullable.control(''),
  });

  constructor(
    private route: ActivatedRoute,
    private invitationsService: EventInvitationsService,
  ) {}

  ngOnInit(): void {
    const parsed = Number.parseInt(this.route.snapshot.paramMap.get('eventId') ?? '', 10);
    this.eventId = Number.isFinite(parsed) && parsed > 0 ? parsed : 0;

    if (!this.eventId) {
      this.loading = false;
      this.error = 'A valid event ID is required.';
      return;
    }

    this.reload();
  }

  submitInvites(): void {
    const emails = this.inviteForm
      .getRawValue()
      .emails.split(/[\n,]/g)
      .map((item) => item.trim())
      .filter(Boolean);
    const userIds = this.inviteForm
      .getRawValue()
      .userIds.split(/[,\s]+/g)
      .map((item) => Number.parseInt(item, 10))
      .filter((item) => Number.isFinite(item) && item > 0);

    this.invitationsService
      .createInvitations(this.eventId, {
        emails,
        userIds,
        expiresAt: this.toIso(this.inviteForm.getRawValue().expiresAt),
      })
      .subscribe({
        next: () => {
          this.inviteForm.patchValue({ emails: '', userIds: '' });
          this.reload();
        },
        error: (err) => {
          this.error = err?.error?.message || 'We could not create invitations.';
        },
      });
  }

  submitLink(): void {
    const value = this.linkForm.getRawValue();
    this.invitationsService
      .createInvitationLink(this.eventId, {
        maxRedemptions: value.maxRedemptions,
        expiresAt: this.toIso(value.expiresAt) ?? '',
      })
      .subscribe({
        next: (link) => {
          this.latestShareUrl = link.shareUrl ?? '';
          this.reload();
        },
        error: (err) => {
          this.error = err?.error?.message || 'We could not create an invitation link.';
        },
      });
  }

  revokeInvitation(invitation: EventInvitation): void {
    this.invitationsService.revokeInvitation(this.eventId, invitation.id).subscribe({
      next: () => this.reload(),
      error: (err) => {
        this.error = err?.error?.message || 'We could not revoke this invitation.';
      },
    });
  }

  revokeLink(link: EventInvitationLink): void {
    this.invitationsService.revokeInvitationLink(this.eventId, link.id).subscribe({
      next: () => this.reload(),
      error: (err) => {
        this.error = err?.error?.message || 'We could not revoke this link.';
      },
    });
  }

  copyShareUrl(url: string | null | undefined): void {
    if (!url || typeof navigator === 'undefined' || !navigator.clipboard) {
      return;
    }

    void navigator.clipboard.writeText(url);
  }

  private reload(): void {
    this.loading = true;
    this.error = '';

    this.invitationsService.getEventInvitations(this.eventId).subscribe({
      next: (invitations) => {
        this.invitations = invitations;
        this.invitationsService.getInvitationLinks(this.eventId).subscribe({
          next: (links) => {
            this.links = links;
            this.loading = false;
          },
          error: (err) => {
            this.loading = false;
            this.error = err?.error?.message || 'We could not load invitation links.';
          },
        });
      },
      error: (err) => {
        this.loading = false;
        this.error = err?.error?.message || 'We could not load invitations.';
      },
    });
  }

  private toIso(value: string): string | null {
    if (!value) {
      return null;
    }

    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? null : date.toISOString();
  }
}
