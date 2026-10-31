import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { provideFeatureFlags } from '@testing';

import { AccountPageComponent } from './account-page.component';

describe('AccountPageComponent', () => {
  function createFixture(
    recentlyViewedEnabled: boolean,
    authEnabled = true,
  ): ComponentFixture<AccountPageComponent> {
    TestBed.configureTestingModule({
      imports: [AccountPageComponent],
      providers: [
        provideRouter([]),
        provideFeatureFlags({
          auth: authEnabled,
          events: true,
          'events.recentlyviewed': recentlyViewedEnabled,
          profile: true,
        }),
      ],
    });

    const fixture = TestBed.createComponent(AccountPageComponent);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => TestBed.resetTestingModule());

  function tabLabels(fixture: ComponentFixture<AccountPageComponent>): string[] {
    return Array.from(fixture.nativeElement.querySelectorAll('nav a')).map((anchor) =>
      (anchor as HTMLElement).textContent!.trim(),
    );
  }

  it('shows the privacy tab when the recently viewed feature is on', () => {
    const fixture = createFixture(true);

    expect(fixture.componentInstance.privacyTabEnabled).toBeTrue();
    expect(tabLabels(fixture)).toContain('Privacy');
  });

  it('hides the privacy tab when the feature is off', () => {
    const fixture = createFixture(false);

    // The tab would otherwise lead to a page with nothing on it.
    expect(fixture.componentInstance.privacyTabEnabled).toBeFalse();
    expect(tabLabels(fixture)).not.toContain('Privacy');
  });

  it('shows the activity tab while the auth feature is on', () => {
    const fixture = createFixture(false);

    expect(fixture.componentInstance.activityTabEnabled).toBeTrue();
    expect(tabLabels(fixture)).toContain('Activity');
  });

  it('hides the activity tab when the auth feature is off', () => {
    const fixture = createFixture(false, false);

    // Its only endpoint is behind the auth feature gate, so the tab could never load.
    expect(fixture.componentInstance.activityTabEnabled).toBeFalse();
    expect(tabLabels(fixture)).not.toContain('Activity');
  });

  it('keeps the other tabs regardless', () => {
    const labels = tabLabels(createFixture(false, false));

    expect(labels).toContain('Profile');
    expect(labels).toContain('Security');
    expect(labels).toContain('Password');
    expect(labels).toContain('Danger Zone');
  });
});
