import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { environment } from '@environments/environment';

import { HomeComponent } from './home.component';

describe('HomeComponent', () => {
  const originalFeatureFlags = environment.featureFlags;

  afterEach(() => {
    environment.featureFlags = { ...originalFeatureFlags };
    TestBed.resetTestingModule();
  });

  it('hides auth and event entry points when those features are disabled', async () => {
    environment.featureFlags = {
      auth: false,
      events: false,
    };

    await TestBed.configureTestingModule({
      imports: [HomeComponent],
      providers: [provideRouter([])],
    }).compileComponents();

    const fixture = TestBed.createComponent(HomeComponent);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;

    expect(element.textContent).not.toContain('Get started');
    expect(element.textContent).not.toContain('Create an account');
    expect(element.textContent).not.toContain('Browse events');
    expect(element.querySelector('input[placeholder="Search artists, teams, venues..."]')).toBeNull();
  });
});
