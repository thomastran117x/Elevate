import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { FavouriteStarComponent } from './favourite-star.component';

describe('FavouriteStarComponent', () => {
  let fixture: ComponentFixture<FavouriteStarComponent>;
  let component: FavouriteStarComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FavouriteStarComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(FavouriteStarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  function button(): HTMLButtonElement {
    return fixture.debugElement.query(By.css('button')).nativeElement as HTMLButtonElement;
  }

  it('reports its pressed state and label to assistive tech', () => {
    expect(button().getAttribute('aria-pressed')).toBe('false');
    expect(button().getAttribute('aria-label')).toBe('Save event for later');

    component.favourited = true;
    fixture.detectChanges();

    expect(button().getAttribute('aria-pressed')).toBe('true');
    expect(button().getAttribute('aria-label')).toBe('Remove from saved events');
  });

  it('uses an explicit label when one is given', () => {
    component.label = 'Save this event';
    fixture.detectChanges();

    expect(button().getAttribute('aria-label')).toBe('Save this event');
  });

  it('emits when clicked', () => {
    const toggled = jasmine.createSpy('toggled');
    component.toggled.subscribe(toggled);

    button().click();

    expect(toggled).toHaveBeenCalledTimes(1);
  });

  it('stops the click reaching a clickable ancestor', () => {
    // The search card wraps the whole tile in a navigation handler, so an unguarded star press
    // would also open the event.
    const event = new MouseEvent('click', { bubbles: true, cancelable: true });
    spyOn(event, 'stopPropagation');
    spyOn(event, 'preventDefault');

    component.activate(event);

    expect(event.stopPropagation).toHaveBeenCalled();
    expect(event.preventDefault).toHaveBeenCalled();
  });

  it('does not emit while a write is in flight', () => {
    const toggled = jasmine.createSpy('toggled');
    component.toggled.subscribe(toggled);
    component.pending = true;
    fixture.detectChanges();

    component.activate(new MouseEvent('click'));

    expect(toggled).not.toHaveBeenCalled();
    expect(button().disabled).toBeTrue();
  });

  it('does not emit when disabled', () => {
    const toggled = jasmine.createSpy('toggled');
    component.toggled.subscribe(toggled);
    component.disabled = true;
    fixture.detectChanges();

    component.activate(new MouseEvent('click'));

    expect(toggled).not.toHaveBeenCalled();
  });

  it('renders a filled star only when favourited', () => {
    const star = () => fixture.debugElement.query(By.css('svg')).nativeElement as SVGElement;

    expect(star().getAttribute('fill')).toBe('none');

    component.favourited = true;
    fixture.detectChanges();

    expect(star().getAttribute('fill')).toBe('currentColor');
  });

  it('shrinks for the small variant', () => {
    component.size = 'sm';
    fixture.detectChanges();

    expect(component.buttonClass).toContain('h-8');
    expect(component.iconClass).toContain('h-4');
  });
});
