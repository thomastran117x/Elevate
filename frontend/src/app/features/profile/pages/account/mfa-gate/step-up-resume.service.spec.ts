import { TestBed } from '@angular/core/testing';

import { STEP_UP_RESUME_PATHS, StepUpResumeService } from './step-up-resume.service';

describe('StepUpResumeService', () => {
  let service: StepUpResumeService;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({});
    service = TestBed.inject(StepUpResumeService);
  });

  afterEach(() => sessionStorage.clear());

  describe('resolve', () => {
    for (const path of STEP_UP_RESUME_PATHS) {
      it(`accepts ${path}`, () => {
        expect(service.resolve(path)).toBe(path);
      });
    }

    it('drops query strings and fragments from an allowlisted path', () => {
      expect(service.resolve('/account/security?tab=totp#setup')).toBe('/account/security');
      expect(service.resolve('  /account/password  ')).toBe('/account/password');
    });

    const rejected = [
      null,
      undefined,
      '',
      '   ',
      '/',
      '/dashboard',
      '/account',
      '/account/security/extra',
      '/account/../admin',
      '/ACCOUNT/SECURITY',
      'account/security',
      '//evil.example/account/security',
      '/\\evil.example',
      'https://evil.example/account/security',
      'javascript:alert(1)',
    ];

    for (const url of rejected) {
      it(`rejects ${JSON.stringify(url)}`, () => {
        expect(service.resolve(url)).toBeNull();
      });
    }
  });

  describe('remember and consume', () => {
    it('round-trips an allowlisted path exactly once', () => {
      service.remember('/account/danger?x=1');

      expect(service.consume()).toBe('/account/danger');
      expect(service.consume()).toBeNull();
    });

    it('never stores a path outside the allowlist', () => {
      service.remember('https://evil.example/account/security');
      service.remember('/admin');

      expect(sessionStorage.length).toBe(0);
      expect(service.consume()).toBeNull();
    });

    it('re-validates a value tampered with in storage', () => {
      sessionStorage.setItem('step_up_resume_path', '//evil.example');

      expect(service.consume()).toBeNull();
      expect(sessionStorage.getItem('step_up_resume_path')).toBeNull();
    });

    it('keeps the most recent path', () => {
      service.remember('/account/security');
      service.remember('/account/password');

      expect(service.consume()).toBe('/account/password');
    });
  });
});
