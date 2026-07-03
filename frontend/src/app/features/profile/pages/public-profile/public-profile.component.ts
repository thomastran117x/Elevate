import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { finalize, switchMap } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../core/api/models/api-client-error.model';
import { PublicProfile, ProfileService } from '../../services/profile.service';

@Component({
  selector: 'app-public-profile',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './public-profile.component.html',
})
export class PublicProfileComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  profile: PublicProfile | null = null;
  loading = true;
  error = '';

  constructor(
    private route: ActivatedRoute,
    private profileService: ProfileService,
  ) {}

  ngOnInit(): void {
    this.route.paramMap
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        switchMap((params) => {
          this.loading = true;
          this.error = '';
          this.profile = null;
          const username = params.get('username') ?? '';
          return this.profileService
            .getPublicProfile(username)
            .pipe(finalize(() => (this.loading = false)));
        }),
      )
      .subscribe({
        next: (profile) => {
          this.profile = profile;
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'This profile could not be found.');
        },
      });
  }

  get userInitials(): string {
    const name = this.profile?.Name || this.profile?.Username || '';
    return name ? name.slice(0, 2).toUpperCase() : '?';
  }

  get usertypeLabel(): string {
    const type = this.profile?.Usertype ?? '';
    return type.charAt(0).toUpperCase() + type.slice(1);
  }
}
