import { CdkDragDrop, DragDropModule, moveItemInArray } from '@angular/cdk/drag-drop';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

import { EventImage } from '../../models/event.types';
import { EventsManagementService } from '../../services/events-management.service';

/**
 * Gallery editor for a saved event: reorder by drag, choose a cover, describe each image, and
 * remove one without touching the rest.
 *
 * Every mutation goes straight to the server and the response replaces {@link images} wholesale,
 * because the server owns two invariants this component cannot enforce locally — exactly one
 * cover, and a promotion when the cover is removed.
 */
@Component({
  selector: 'app-event-gallery-manager',
  standalone: true,
  imports: [DragDropModule, FormsModule],
  templateUrl: './event-gallery-manager.component.html',
})
export class EventGalleryManagerComponent implements OnChanges {
  @Input({ required: true }) eventId!: number;
  @Input() images: EventImage[] = [];
  @Input() maxImages = 5;

  @Output() imagesChange = new EventEmitter<EventImage[]>();

  busyImageId: number | null = null;
  reordering = false;
  error: string | null = null;

  /** Alt-text drafts, so typing does not fire a request per keystroke. */
  altDrafts = new Map<number, string>();

  constructor(private readonly managementService: EventsManagementService) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['images']) {
      this.syncDrafts();
    }
  }

  get isFull(): boolean {
    return this.images.length >= this.maxImages;
  }

  get imagesNeedingAltText(): number {
    return this.images.filter((image) => image.needsAltText).length;
  }

  trackById(_index: number, image: EventImage): number {
    return image.id;
  }

  async onDrop(event: CdkDragDrop<EventImage[]>): Promise<void> {
    if (event.previousIndex === event.currentIndex) return;

    const reordered = [...this.images];
    moveItemInArray(reordered, event.previousIndex, event.currentIndex);

    // Shown in the new order straight away; the server response then replaces it, which also
    // puts the view back if the request turns out to fail.
    this.images = reordered;
    this.reordering = true;
    this.error = null;

    try {
      const saved = await firstValueFrom(
        this.managementService.reorderEventImages(
          this.eventId,
          reordered.map((image) => image.id),
        ),
      );
      this.apply(saved);
    } catch {
      this.error = 'The new order could not be saved.';
      await this.refresh();
    } finally {
      this.reordering = false;
    }
  }

  async setCover(image: EventImage): Promise<void> {
    if (image.isCover) return;

    await this.mutate(
      image.id,
      async () => {
        const saved = await firstValueFrom(
          this.managementService.setEventCoverImage(this.eventId, image.id),
        );
        this.apply(saved);
      },
      'The cover image could not be changed.',
    );
  }

  async saveAltText(image: EventImage): Promise<void> {
    const draft = (this.altDrafts.get(image.id) ?? '').trim();
    if (draft === (image.altText ?? '') && !image.isDecorative) return;

    await this.mutate(
      image.id,
      async () => {
        const saved = await firstValueFrom(
          this.managementService.updateEventImage(this.eventId, image.id, {
            altText: draft,
            isDecorative: false,
          }),
        );
        this.replaceOne(saved);
      },
      'The description could not be saved.',
    );
  }

  async toggleDecorative(image: EventImage, isDecorative: boolean): Promise<void> {
    await this.mutate(
      image.id,
      async () => {
        const saved = await firstValueFrom(
          this.managementService.updateEventImage(this.eventId, image.id, {
            // Marking an image decorative clears its alt text: the server rejects holding both.
            altText: isDecorative ? null : (this.altDrafts.get(image.id) ?? ''),
            isDecorative,
          }),
        );
        this.replaceOne(saved);
      },
      'The image could not be updated.',
    );
  }

  async remove(image: EventImage): Promise<void> {
    await this.mutate(
      image.id,
      async () => {
        await firstValueFrom(this.managementService.removeEventImage(this.eventId, image.id));
        await this.refresh();
      },
      'The image could not be removed.',
    );
  }

  private async refresh(): Promise<void> {
    this.apply(await firstValueFrom(this.managementService.getEventImages(this.eventId)));
  }

  private apply(images: EventImage[]): void {
    this.images = images;
    this.syncDrafts();
    this.imagesChange.emit(images);
  }

  private replaceOne(image: EventImage): void {
    this.apply(this.images.map((item) => (item.id === image.id ? image : item)));
  }

  /** Keeps the drafts in step with the server's copy, dropping any for images that are gone. */
  private syncDrafts(): void {
    const next = new Map<number, string>();
    for (const image of this.images) {
      next.set(image.id, image.altText ?? '');
    }
    this.altDrafts = next;
  }

  private async mutate(
    imageId: number,
    action: () => Promise<void>,
    failureMessage: string,
  ): Promise<void> {
    this.busyImageId = imageId;
    this.error = null;

    try {
      await action();
    } catch {
      this.error = failureMessage;
    } finally {
      this.busyImageId = null;
    }
  }
}
