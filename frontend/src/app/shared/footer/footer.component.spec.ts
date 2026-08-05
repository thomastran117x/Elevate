import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { provideFeatureFlags } from '@testing';

import { FooterComponent } from './footer.component';
import { FeatureFlags } from '../../core/features/feature-flags.types';

describe('FooterComponent', () => {
  function create(flags: FeatureFlags = {}): FooterComponent {
    TestBed.configureTestingModule({
      imports: [FooterComponent],
      providers: [provideRouter([]), provideFeatureFlags(flags)],
    });

    return TestBed.createComponent(FooterComponent).componentInstance;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('shows the current year', () => {
    expect(create().currentYear).toBe(new Date().getFullYear());
  });

  it('enables every section by default', () => {
    const footer = create();

    expect(footer.authEnabled).toBeTrue();
    expect(footer.eventsEnabled).toBeTrue();
    expect(footer.clubsEnabled).toBeTrue();
  });

  it('reads each flag independently', () => {
    const footer = create({ auth: false, clubs: false });

    expect(footer.authEnabled).toBeFalse();
    expect(footer.clubsEnabled).toBeFalse();
    expect(footer.eventsEnabled).toBeTrue();
  });

  it('lists the social links with a label, href and icon path', () => {
    const footer = create();

    expect(footer.socials.map((s) => s.label)).toEqual(['GitHub', 'X', 'LinkedIn']);
    for (const social of footer.socials) {
      expect(social.href).toMatch(/^https:\/\//);
      expect(social.path.length).toBeGreaterThan(0);
    }
  });
});
