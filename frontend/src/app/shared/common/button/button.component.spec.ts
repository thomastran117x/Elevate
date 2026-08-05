import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AppButtonComponent, AppButtonSize, AppButtonVariant } from './button.component';

describe('AppButtonComponent', () => {
  function create(): AppButtonComponent {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [AppButtonComponent],
      providers: [provideRouter([])],
    });

    return TestBed.createComponent(AppButtonComponent).componentInstance;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('defaults to a medium primary button', () => {
    const classes = create().classes;

    expect(classes).toContain('cta-solid');
    expect(classes).toContain('px-4 py-2.5 text-sm');
    expect(classes).not.toContain('cursor-not-allowed');
  });

  const variants: Array<[AppButtonVariant, string]> = [
    ['primary', 'cta-solid'],
    ['secondary', 'cta-subtle'],
    ['ghost', 'cta-ghost'],
    ['danger', 'text-danger'],
  ];

  for (const [variant, expected] of variants) {
    it(`styles the ${variant} variant`, () => {
      const button = create();
      button.variant = variant;

      expect(button.classes).toContain(expected);
    });
  }

  const sizes: Array<[AppButtonSize, string]> = [
    ['sm', 'px-3 py-2 text-xs'],
    ['md', 'px-4 py-2.5 text-sm'],
    ['lg', 'px-5 py-3 text-sm'],
  ];

  for (const [size, expected] of sizes) {
    it(`sizes the ${size} button`, () => {
      const button = create();
      button.size = size;

      expect(button.classes).toContain(expected);
    });
  }

  it('adds the disabled styling and blocks pointer events', () => {
    const button = create();
    button.disabled = true;

    expect(button.classes).toContain('cursor-not-allowed');
    expect(button.classes).toContain('pointer-events-none');
  });

  it('appends any caller-supplied classes', () => {
    const button = create();
    button.className = 'w-full';

    expect(button.classes).toContain('w-full');
  });

  it('leaves no trailing whitespace when nothing extra is applied', () => {
    const classes = create().classes;

    expect(classes).toBe(classes.trim());
  });
});
