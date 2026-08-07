import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ElementRef, PLATFORM_ID } from '@angular/core';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { makeCurrentUser, provideFeatureFlags, provideTestStore } from '@testing';

import { NavbarComponent } from './navbar.component';
import { AuthService } from '../../features/auth/services/auth.service';
import { AuthTokenService } from '../../core/api/services/auth-token.service';
import { ThemeService } from '../../core/services/theme.service';
import { FeatureFlags } from '../../core/features/feature-flags.types';
import { User } from '../../core/stores/user.model';

describe('NavbarComponent', () => {
  let fixture: ComponentFixture<NavbarComponent>;
  let component: NavbarComponent;
  let auth: jasmine.SpyObj<Pick<AuthService, 'logout'>>;
  let authToken: { logoutLocal: jasmine.Spy };
  let theme: { toggle: jasmine.Spy; isDark: () => boolean };

  async function setup(
    opts: { user?: User | null; flags?: FeatureFlags; platform?: string } = {},
  ): Promise<void> {
    TestBed.resetTestingModule();

    auth = jasmine.createSpyObj<Pick<AuthService, 'logout'>>('AuthService', ['logout']);
    auth.logout.and.returnValue(of(undefined as void));
    authToken = { logoutLocal: jasmine.createSpy('logoutLocal') };
    theme = { toggle: jasmine.createSpy('toggle'), isDark: () => false };

    await TestBed.configureTestingModule({
      imports: [NavbarComponent],
      providers: [
        provideRouter([]),
        ...provideTestStore({ user: opts.user ?? null }),
        provideFeatureFlags(opts.flags ?? {}),
        { provide: AuthService, useValue: auth },
        { provide: AuthTokenService, useValue: authToken },
        { provide: ThemeService, useValue: theme },
        { provide: PLATFORM_ID, useValue: opts.platform ?? 'browser' },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(NavbarComponent);
    component = fixture.componentInstance;
  }

  beforeEach(async () => {
    await setup();
  });

  afterEach(() => {
    document.body.style.overflow = '';
    TestBed.resetTestingModule();
  });

  describe('feature flags', () => {
    it('enables every section by default', () => {
      expect(component.authEnabled).toBeTrue();
      expect(component.eventsEnabled).toBeTrue();
      expect(component.clubsEnabled).toBeTrue();
      expect(component.invitationsEnabled).toBeTrue();
      expect(component.waitlistEnabled).toBeTrue();
      expect(component.favouritesEnabled).toBeTrue();
    });

    it('reads each flag independently', async () => {
      await setup({ flags: { auth: false, 'events.waitlist': false } });

      expect(component.authEnabled).toBeFalse();
      expect(component.waitlistEnabled).toBeFalse();
      expect(component.eventsEnabled).toBeTrue();
      expect(component.invitationsEnabled).toBeTrue();
      expect(component.favouritesEnabled).toBeTrue();
    });

    it('cascades a disabled parent to its event sub-features', async () => {
      await setup({ flags: { events: false } });

      expect(component.eventsEnabled).toBeFalse();
      expect(component.invitationsEnabled).toBeFalse();
      expect(component.waitlistEnabled).toBeFalse();
      expect(component.favouritesEnabled).toBeFalse();
    });
  });

  it('exposes the signed-in user from the store', async () => {
    await setup({ user: makeCurrentUser({ Username: 'member' }) });

    let user: User | null = null;
    component.user$.subscribe((value) => (user = value));

    expect(user).toEqual(jasmine.objectContaining({ Username: 'member' }));
  });

  describe('scroll state', () => {
    afterEach(() => window.scrollTo(0, 0));

    it('marks the bar scrolled past 16px', () => {
      spyOnProperty(window, 'scrollY').and.returnValue(40);
      component.onScroll();
      expect(component.scrolled).toBeTrue();
    });

    it('leaves it unmarked at the top of the page', () => {
      spyOnProperty(window, 'scrollY').and.returnValue(4);
      component.onScroll();
      expect(component.scrolled).toBeFalse();
    });
  });

  describe('menus', () => {
    it('opens the mobile menu and locks page scrolling', () => {
      component.toggleMobile();

      expect(component.mobileOpen).toBeTrue();
      expect(document.body.style.overflow).toBe('hidden');
    });

    it('releases the scroll lock when the mobile menu closes', () => {
      component.toggleMobile();
      component.toggleMobile();

      expect(component.mobileOpen).toBeFalse();
      expect(document.body.style.overflow).toBe('');
    });

    it('never touches the document on the server', async () => {
      await setup({ platform: 'server' });
      document.body.style.overflow = 'scroll';

      component.toggleMobile();

      expect(component.mobileOpen).toBeTrue();
      expect(document.body.style.overflow).toBe('scroll');
    });

    it('keeps the two menus mutually exclusive', () => {
      component.toggleUserMenu();
      expect(component.userMenuOpen).toBeTrue();

      component.toggleMobile();
      expect(component.mobileOpen).toBeTrue();
      expect(component.userMenuOpen).toBeFalse();

      component.toggleUserMenu();
      expect(component.userMenuOpen).toBeTrue();
      expect(component.mobileOpen).toBeFalse();
    });

    it('closes both menus on Escape', () => {
      component.toggleUserMenu();

      component.onEscape();

      expect(component.userMenuOpen).toBeFalse();
      expect(component.mobileOpen).toBeFalse();
    });

    it('ignores document clicks while both menus are closed', () => {
      const host = fixture.debugElement.injector.get(ElementRef) as ElementRef<HTMLElement>;
      spyOn(host.nativeElement, 'contains');

      component.onDocumentClick(new MouseEvent('click'));

      expect(host.nativeElement.contains).not.toHaveBeenCalled();
    });

    it('closes an open menu when the click lands outside the navbar', () => {
      component.toggleUserMenu();
      const outside = document.createElement('div');
      document.body.appendChild(outside);

      component.onDocumentClick({ target: outside } as unknown as MouseEvent);

      expect(component.userMenuOpen).toBeFalse();
      outside.remove();
    });

    it('leaves an open menu alone when the click is inside the navbar', () => {
      component.toggleUserMenu();
      const inside = fixture.nativeElement as HTMLElement;

      component.onDocumentClick({ target: inside } as unknown as MouseEvent);

      expect(component.userMenuOpen).toBeTrue();
    });
  });

  it('delegates the theme toggle', () => {
    component.toggleTheme();

    expect(theme.toggle).toHaveBeenCalledTimes(1);
  });

  describe('logout', () => {
    it('closes the menus and clears the local session', () => {
      component.toggleUserMenu();

      component.logout();

      expect(component.userMenuOpen).toBeFalse();
      expect(auth.logout).toHaveBeenCalledTimes(1);
      expect(authToken.logoutLocal).toHaveBeenCalledTimes(1);
    });

    it('clears the local session even when the server call fails', () => {
      auth.logout.and.returnValue(throwError(() => new Error('offline')));

      component.logout();

      expect(authToken.logoutLocal).toHaveBeenCalledTimes(1);
    });
  });
});
