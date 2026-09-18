import { CdkDragDrop } from '@angular/cdk/drag-drop';
import { of, throwError } from 'rxjs';

import { EventImage } from '../../models/event.types';
import { EventsManagementService } from '../../services/events-management.service';
import { EventGalleryManagerComponent } from './event-gallery-manager.component';

function makeImage(overrides: Partial<EventImage> = {}): EventImage {
  return {
    id: 1,
    url: 'https://cdn.test/a.png',
    altText: null,
    isDecorative: false,
    isCover: false,
    sortOrder: 0,
    needsAltText: true,
    createdAt: '2026-05-01T00:00:00Z',
    updatedAt: '2026-05-01T00:00:00Z',
    ...overrides,
  };
}

function drop(previousIndex: number, currentIndex: number): CdkDragDrop<EventImage[]> {
  return { previousIndex, currentIndex } as CdkDragDrop<EventImage[]>;
}

describe('EventGalleryManagerComponent', () => {
  let component: EventGalleryManagerComponent;
  let service: jasmine.SpyObj<EventsManagementService>;
  let emitted: EventImage[][];

  const first = makeImage({ id: 1, url: 'https://cdn.test/a.png', sortOrder: 0, isCover: true });
  const second = makeImage({ id: 2, url: 'https://cdn.test/b.png', sortOrder: 1 });
  const third = makeImage({ id: 3, url: 'https://cdn.test/c.png', sortOrder: 2 });

  beforeEach(() => {
    service = jasmine.createSpyObj<EventsManagementService>('EventsManagementService', [
      'getEventImages',
      'updateEventImage',
      'reorderEventImages',
      'setEventCoverImage',
      'removeEventImage',
    ]);

    component = new EventGalleryManagerComponent(service);
    component.eventId = 12;
    component.images = [first, second, third];
    component.ngOnChanges({ images: {} as never });

    emitted = [];
    component.imagesChange.subscribe((images) => emitted.push(images));
  });

  it('counts the images still needing a description', () => {
    expect(component.imagesNeedingAltText).toBe(3);

    component.images = [makeImage({ needsAltText: false }), makeImage({ id: 2 })];
    expect(component.imagesNeedingAltText).toBe(1);
  });

  describe('reordering', () => {
    it('sends the full id order and adopts the gallery the server returns', async () => {
      const reordered = [third, first, second];
      service.reorderEventImages.and.returnValue(of(reordered));

      await component.onDrop(drop(2, 0));

      expect(service.reorderEventImages).toHaveBeenCalledWith(12, [3, 1, 2]);
      expect(component.images).toEqual(reordered);
      expect(emitted.at(-1)).toEqual(reordered);
    });

    it('does nothing when the image was dropped where it started', async () => {
      await component.onDrop(drop(1, 1));

      expect(service.reorderEventImages).not.toHaveBeenCalled();
    });

    it('reloads the gallery when the reorder fails, so the view does not keep a lie', async () => {
      service.reorderEventImages.and.returnValue(throwError(() => new Error('nope')));
      service.getEventImages.and.returnValue(of([first, second, third]));

      await component.onDrop(drop(2, 0));

      expect(component.error).toBe('The new order could not be saved.');
      expect(service.getEventImages).toHaveBeenCalledWith(12);
      expect(component.images).toEqual([first, second, third]);
      expect(component.reordering).toBeFalse();
    });
  });

  describe('cover', () => {
    it('replaces the whole gallery so exactly one cover survives', async () => {
      const updated = [{ ...first, isCover: false }, { ...second, isCover: true }, third];
      service.setEventCoverImage.and.returnValue(of(updated));

      await component.setCover(second);

      expect(service.setEventCoverImage).toHaveBeenCalledWith(12, 2);
      expect(component.images.filter((image) => image.isCover).length).toBe(1);
    });

    it('does not ask again for the image that is already the cover', async () => {
      await component.setCover(first);

      expect(service.setEventCoverImage).not.toHaveBeenCalled();
    });
  });

  describe('alt text', () => {
    it('saves the draft and replaces only that image', async () => {
      const described = { ...second, altText: 'Main stage', needsAltText: false };
      service.updateEventImage.and.returnValue(of(described));
      component.altDrafts.set(2, '  Main stage  ');

      await component.saveAltText(second);

      expect(service.updateEventImage).toHaveBeenCalledWith(12, 2, {
        altText: 'Main stage',
        isDecorative: false,
      });
      expect(component.images.find((image) => image.id === 2)?.altText).toBe('Main stage');
      expect(component.images.find((image) => image.id === 1)).toEqual(first);
    });

    it('skips the request when the draft matches what is already saved', async () => {
      const described = makeImage({ id: 4, altText: 'Unchanged', needsAltText: false });
      component.images = [described];
      component.ngOnChanges({ images: {} as never });

      await component.saveAltText(described);

      expect(service.updateEventImage).not.toHaveBeenCalled();
    });

    it('clears alt text when the image is marked decorative', async () => {
      service.updateEventImage.and.returnValue(
        of({ ...second, isDecorative: true, altText: null, needsAltText: false }),
      );
      component.altDrafts.set(2, 'Some text');

      await component.toggleDecorative(second, true);

      expect(service.updateEventImage).toHaveBeenCalledWith(12, 2, {
        altText: null,
        isDecorative: true,
      });
    });

    describe('un-marking decorative', () => {
      const decorative = makeImage({
        id: 7,
        isDecorative: true,
        altText: null,
        needsAltText: false,
      });

      beforeEach(() => {
        component.images = [decorative];
        component.ngOnChanges({ images: {} as never });
      });

      it('opens the description field instead of saving an edit the server would reject', async () => {
        await component.toggleDecorative(decorative, false);

        expect(service.updateEventImage).not.toHaveBeenCalled();
        expect(component.isDescribing(decorative)).toBeTrue();
      });

      it('leaves the image decorative when nothing is typed', async () => {
        await component.toggleDecorative(decorative, false);
        await component.saveAltText(decorative);

        expect(service.updateEventImage).not.toHaveBeenCalled();
        expect(component.isDescribing(decorative)).toBeTrue();
      });

      it('commits both changes once a description is written', async () => {
        service.updateEventImage.and.returnValue(
          of({
            ...decorative,
            isDecorative: false,
            altText: 'A quiet corner',
            needsAltText: false,
          }),
        );

        await component.toggleDecorative(decorative, false);
        component.altDrafts.set(7, 'A quiet corner');
        await component.saveAltText(decorative);

        expect(service.updateEventImage).toHaveBeenCalledWith(12, 7, {
          altText: 'A quiet corner',
          isDecorative: false,
        });
        expect(component.isDescribing(decorative)).toBeFalse();
      });

      it('keeps what was typed when the gallery refreshes mid-edit', async () => {
        await component.toggleDecorative(decorative, false);
        component.altDrafts.set(7, 'Half a thought');

        // A refresh still reports the image as decorative with no alt text.
        component.images = [decorative];
        component.ngOnChanges({ images: {} as never });

        expect(component.altDrafts.get(7)).toBe('Half a thought');
      });
    });

    it('surfaces a failure without dropping the gallery', async () => {
      service.updateEventImage.and.returnValue(throwError(() => new Error('nope')));
      component.altDrafts.set(2, 'Main stage');

      await component.saveAltText(second);

      expect(component.error).toBe('The description could not be saved.');
      expect(component.images.length).toBe(3);
      expect(component.busyImageId).toBeNull();
    });
  });

  it('reports when the gallery is full', () => {
    component.maxImages = 3;
    expect(component.isFull).toBeTrue();

    component.images = [first];
    expect(component.isFull).toBeFalse();
  });

  it('tracks images by id so a reorder moves nodes instead of rebuilding them', () => {
    expect(component.trackById(0, second)).toBe(2);
  });

  it('re-saves a described image when it is being un-marked as decorative', async () => {
    const decorative = makeImage({ id: 5, isDecorative: true, altText: null, needsAltText: false });
    component.images = [decorative];
    component.ngOnChanges({ images: {} as never });
    component.altDrafts.set(5, '');
    service.updateEventImage.and.returnValue(
      of({ ...decorative, isDecorative: false, needsAltText: true }),
    );

    // The draft matches the saved alt text, but the image is decorative, so this is still a change.
    await component.saveAltText(decorative);

    expect(service.updateEventImage).toHaveBeenCalledWith(12, 5, {
      altText: '',
      isDecorative: false,
    });
  });

  describe('removal', () => {
    it('reloads afterwards, because the server may have moved the cover', async () => {
      service.removeEventImage.and.returnValue(of({}));
      service.getEventImages.and.returnValue(of([{ ...second, isCover: true }, third]));

      await component.remove(first);

      expect(service.removeEventImage).toHaveBeenCalledWith(12, 1);
      expect(component.images.map((image) => image.id)).toEqual([2, 3]);
      expect(component.images.filter((image) => image.isCover).length).toBe(1);
    });
  });

  it('drops alt-text drafts for images that are no longer in the gallery', () => {
    component.altDrafts.set(99, 'stale');
    component.images = [first];
    component.ngOnChanges({ images: {} as never });

    expect(component.altDrafts.has(99)).toBeFalse();
    expect(component.altDrafts.has(1)).toBeTrue();
  });
});
