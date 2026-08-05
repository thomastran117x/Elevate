import { EnvironmentProviders, Provider, ProviderToken } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

export type TestProvider = Provider | EnvironmentProviders;

/**
 * The provider pair every HTTP-backed spec needs. Kept as one call so specs
 * cannot accidentally register the testing backend without the real client.
 */
export function provideHttpTesting(): TestProvider[] {
  return [provideHttpClient(), provideHttpClientTesting()];
}

export interface ServiceHarness<T> {
  service: T;
  httpMock: HttpTestingController;
}

/**
 * Configures a TestBed for a plain HTTP service and hands back both the service
 * and the testing backend. Call `httpMock.verify()` in `afterEach`.
 */
export function setupService<T>(
  token: ProviderToken<T>,
  extraProviders: TestProvider[] = [],
): ServiceHarness<T> {
  TestBed.configureTestingModule({
    providers: [token as Provider, ...provideHttpTesting(), ...extraProviders],
  });

  return {
    service: TestBed.inject(token),
    httpMock: TestBed.inject(HttpTestingController),
  };
}
