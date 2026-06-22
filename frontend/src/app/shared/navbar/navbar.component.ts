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

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './navbar.component.html',
})
export class NavbarComponent {
  scrolled = false;
  customersOpen = false;
  userMenuOpen = false;
  mobileOpen = false;
  user$: Observable<User | null>;
  isCollapsed = true;
  readonly authEnabled: boolean;
  readonly invitationsEnabled: boolean;

  constructor(
    private store: Store<{ user: UserState }>,
    private auth: AuthService,
    private authToken: AuthTokenService,
    private featureFlags: FeatureFlagsService,
  ) {
    this.user$ = this.store.select(selectUser);
    this.authEnabled = this.featureFlags.isEnabled(FEATURE_KEYS.auth);
    this.invitationsEnabled = this.featureFlags.isEnabled(FEATURE_KEYS.eventsInvitations);
  }

  @HostListener('window:scroll')
  onScroll() {
    this.scrolled = window.scrollY > 40;
  }

  toggleMobile() {
    this.mobileOpen = !this.mobileOpen;
  }

  toggleCustomers() {
    this.customersOpen = !this.customersOpen;
    this.userMenuOpen = false;
  }

  toggleUserMenu() {
    this.userMenuOpen = !this.userMenuOpen;
    this.customersOpen = false;
  }

  closeAllDropdowns() {
    this.customersOpen = false;
    this.userMenuOpen = false;
  }

  logout() {
    this.auth.logout().subscribe({
      next: () => this.authToken.logoutLocal(),
      error: () => this.authToken.logoutLocal(),
    });
  }
}
