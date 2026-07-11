import { CommonModule } from '@angular/common';
import { Component, HostListener } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { Observable } from 'rxjs';
import { selectUser } from '../../core/stores/user.selectors';
import { User } from '../../core/stores/user.model';
import { UserState } from '../../core/stores/user.reducer';
import { AuthService } from '../../features/auth/services/auth.service';
import { AuthTokenService } from '../../core/api/services/auth-token.service';
import { FeatureFlagsService } from '../../core/features/feature-flags.service';
import { FEATURE_KEYS } from '../../core/features/feature-flags.types';
import { ThemeService } from '../../core/services/theme.service';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './navbar.component.html',
})
export class NavbarComponent {
  scrolled = false;
  userMenuOpen = false;
  mobileOpen = false;
  user$: Observable<User | null>;
  readonly authEnabled: boolean;
  readonly invitationsEnabled: boolean;
  readonly eventsEnabled: boolean;
  readonly clubsEnabled: boolean;

  constructor(
    private store: Store<{ user: UserState }>,
    private auth: AuthService,
    private authToken: AuthTokenService,
    private featureFlags: FeatureFlagsService,
    protected theme: ThemeService,
  ) {
    this.user$ = this.store.select(selectUser);
    this.authEnabled = this.featureFlags.isEnabled(FEATURE_KEYS.auth);
    this.invitationsEnabled = this.featureFlags.isEnabled(FEATURE_KEYS.eventsInvitations);
    this.eventsEnabled = this.featureFlags.isEnabled(FEATURE_KEYS.events);
    this.clubsEnabled = this.featureFlags.isEnabled(FEATURE_KEYS.clubs);
  }

  @HostListener('window:scroll')
  onScroll() {
    this.scrolled = window.scrollY > 16;
  }

  toggleMobile() {
    this.mobileOpen = !this.mobileOpen;
    this.userMenuOpen = false;
  }

  toggleTheme() {
    this.theme.toggle();
  }

  toggleUserMenu() {
    this.userMenuOpen = !this.userMenuOpen;
  }

  logout() {
    this.auth.logout().subscribe({
      next: () => this.authToken.logoutLocal(),
      error: () => this.authToken.logoutLocal(),
    });
  }
}
