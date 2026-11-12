import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';

import { flushPromises } from '@testing';

import { TrustedDevicesCardComponent } from './trusted-devices-card.component';
import {
  ApiClientClientError,
  MFA_REQUIRED_ERROR_CODE,
} from '../../../../../../../core/api/models/api-client-error.model';
import {
  AccountDevicesService,
  TrustedDevice,
} from '../../../../../services/account-devices.service';
import { MfaGateComponent } from '../../../mfa-gate/mfa-gate.component';

@Component({ selector: 'app-mfa-gate', standalone: true, template: '' })
class StubMfaGateComponent {
  @Input() modalOnly = false;
  @Output() verified = new EventEmitter<void>();
  @Output() dismissed = new EventEmitter<void>();
}

function device(overrides: Partial<TrustedDevice> = {}): TrustedDevice {
  return {
    id: 2,
    current: false,
    client: 'safari',
    deviceClass: 'tablet',
    network: '198.51.100.0/24',
    createdAt: '2026-10-01T09:00:00Z',
    lastSeenAt: '2026-11-10T09:00:00Z',
    expiresAt: '2026-12-30T09:00:00Z',
    ...overrides,
  };
}

const CURRENT = device({
  id: 1,
  current: true,
  client: 'chrome',
  deviceClass: 'desktop',
  network: null,
});
const OTHER = device();

describe('TrustedDevicesCardComponent', () => {
  let fixture: ComponentFixture<TrustedDevicesCardComponent>;
  let devices: jasmine.SpyObj<AccountDevicesService>;

  async function create(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [TrustedDevicesCardComponent],
      providers: [{ provide: AccountDevicesService, useValue: devices }],
    })
      .overrideComponent(TrustedDevicesCardComponent, {
        remove: { imports: [MfaGateComponent] },
        add: { imports: [StubMfaGateComponent] },
      })
      .compileComponents();

    fixture = TestBed.createComponent(TrustedDevicesCardComponent);
    fixture.detectChanges();
    await settle();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function row(id: number): HTMLElement {
    const match = el().querySelector<HTMLElement>(`[data-testid="trusted-device-${id}"]`);
    expect(match).withContext(`device ${id}`).not.toBeNull();
    return match!;
  }

  function button(label: string, root: ParentNode = el()): HTMLButtonElement {
    const match = Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
    expect(match).withContext(`button "${label}"`).toBeDefined();
    return match!;
  }

  function dialog(): HTMLElement | null {
    return el().querySelector('[role="dialog"]');
  }

  function banner(): string {
    return el().querySelector('[data-testid="trusted-devices-banner"]')?.textContent?.trim() ?? '';
  }

  function gate(): StubMfaGateComponent | null {
    return (
      fixture.debugElement.query(By.directive(StubMfaGateComponent))?.componentInstance ?? null
    );
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await flushPromises();
    fixture.detectChanges();
  }

  async function confirm(label: string): Promise<void> {
    await settle();
    button(label, dialog()!).click();
    await settle();
  }

  beforeEach(() => {
    devices = jasmine.createSpyObj<AccountDevicesService>('AccountDevicesService', [
      'listTrustedDevices',
      'revokeTrustedDevice',
      'revokeAllTrustedDevices',
    ]);
    devices.listTrustedDevices.and.returnValue(of([CURRENT, OTHER]));
  });

  it('lists each trusted device with its expiry, marking this one', async () => {
    await create();

    expect(row(1).textContent).toContain('Chrome · Desktop');
    expect(row(1).textContent).toContain('This device');
    expect(row(1).textContent).toContain('Trusted until');
    expect(row(2).textContent).toContain('Safari · Tablet');
    expect(row(2).textContent).toContain('last seen from 198.51.100.0/24');
    expect(row(2).querySelector('button')!.getAttribute('aria-label')).toBe(
      'Forget Safari · Tablet',
    );
  });

  it('explains an empty list and offers nothing to forget', async () => {
    devices.listTrustedDevices.and.returnValue(of([]));
    await create();

    expect(el().textContent).toContain('No trusted devices.');
    expect(el().textContent).not.toContain('Forget all devices');
  });

  it('offers to retry when the list cannot load', async () => {
    devices.listTrustedDevices.and.returnValue(throwError(() => new Error('Network down')));
    await create();

    expect(el().querySelector('[role="alert"]')?.textContent).toContain('Network down');
    devices.listTrustedDevices.and.returnValue(of([OTHER]));
    button('Try again').click();
    await settle();

    expect(row(2)).toBeTruthy();
  });

  it('asks before forgetting a device, and says no one is signed out', async () => {
    devices.revokeTrustedDevice.and.returnValue(
      of({ revokedCount: 1, currentDeviceRevoked: false }),
    );
    await create();

    row(2).querySelector('button')!.click();
    await settle();
    expect(dialog()?.textContent).toContain('Forget Safari · Tablet?');
    expect(dialog()?.textContent).toContain('No one is signed out.');
    devices.listTrustedDevices.and.returnValue(of([CURRENT]));
    await confirm('Forget device');

    expect(devices.revokeTrustedDevice).toHaveBeenCalledWith(2);
    expect(banner()).toBe('Safari · Tablet was forgotten.');
    expect(el().querySelector('[data-testid="trusted-device-2"]')).toBeNull();
  });

  it('tells the user this browser will verify again after forgetting it', async () => {
    devices.revokeTrustedDevice.and.returnValue(
      of({ revokedCount: 1, currentDeviceRevoked: true }),
    );
    await create();

    row(1).querySelector('button')!.click();
    await settle();
    expect(dialog()?.textContent).toContain('Forget this device?');
    await confirm('Forget device');

    expect(banner()).toContain('This device was forgotten.');
  });

  it('forgets every device', async () => {
    devices.revokeAllTrustedDevices.and.returnValue(
      of({ revokedCount: 2, currentDeviceRevoked: true }),
    );
    await create();

    button('Forget all devices').click();
    await confirm('Forget all');

    expect(devices.revokeAllTrustedDevices).toHaveBeenCalled();
    expect(banner()).toBe('2 trusted devices were forgotten.');
  });

  it('does nothing when the confirmation is cancelled', async () => {
    await create();

    button('Forget all devices').click();
    await confirm('Keep trusting');

    expect(devices.revokeAllTrustedDevices).not.toHaveBeenCalled();
  });

  it('verifies and then finishes a removal refused for a missing step-up', async () => {
    devices.revokeTrustedDevice.and.returnValues(
      throwError(() => new ApiClientClientError('Verify again', 403, MFA_REQUIRED_ERROR_CODE)),
      of({ revokedCount: 1, currentDeviceRevoked: false }),
    );
    await create();

    row(2).querySelector('button')!.click();
    await confirm('Forget device');
    expect(gate()?.modalOnly).toBeTrue();

    gate()!.verified.emit();
    await settle();

    expect(devices.revokeTrustedDevice).toHaveBeenCalledTimes(2);
    expect(banner()).toBe('Safari · Tablet was forgotten.');
  });

  it('reports any other failure', async () => {
    devices.revokeAllTrustedDevices.and.returnValue(throwError(() => new Error('Network down')));
    await create();

    button('Forget all devices').click();
    await confirm('Forget all');

    expect(banner()).toBe('Network down');
  });
});
