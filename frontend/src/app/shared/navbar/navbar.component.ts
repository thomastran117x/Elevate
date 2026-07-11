import { CommonModule, isPlatformBrowser } from '@angular/common';
import { Component, ElementRef, HostListener, Inject, PLATFORM_ID } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
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
  imports: [CommonModule, RouterLink, RouterLinkActive],
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
    private host: ElementRef<HTMLElement>,
    @Inject(PLATFORM_ID) private platformId: object,
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

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (!this.mobileOpen && !this.userMenuOpen) {
      return;
    }
    if (!this.host.nativeElement.contains(event.target as Node)) {
      this.closeMenus();
    }
  }

  @HostListener('document:keydown.escape')
  onEscape() {
    this.closeMenus();
  }

  toggleMobile() {
    this.mobileOpen = !this.mobileOpen;
    this.userMenuOpen = false;
    this.syncScrollLock();
  }

  closeMenus() {
    this.mobileOpen = false;
    this.userMenuOpen = false;
    this.syncScrollLock();
  }

  private syncScrollLock() {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }
    document.body.style.overflow = this.mobileOpen ? 'hidden' : '';
  }

  toggleTheme() {
    this.theme.toggle();
  }

  toggleUserMenu() {
    this.userMenuOpen = !this.userMenuOpen;
    this.mobileOpen = false;
  }

  logout() {
    this.closeMenus();
    this.auth.logout().subscribe({
      next: () => this.authToken.logoutLocal(),
      error: () => this.authToken.logoutLocal(),
    });
  }
}
