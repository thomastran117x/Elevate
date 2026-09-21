import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs/operators';

import { getApiClientMessage } from '../../../../../core/api/models/api-client-error.model';
import { EventsManagementService } from '../../../../events/services/events-management.service';
import { CanComponentDeactivate } from '../../../guards/unsaved-changes.guard';
import { ALL_CLUB_TYPES, ClubType } from '../../../models/club.types';
import { toClubtypeAlias } from '../../../models/club-management.types';
import { ClubManagementService } from '../../../services/club-management.service';
import { ClubsService } from '../../../services/clubs.service';
import { IMAGE_ACCEPT, screenImageFile } from '@shared/upload/image-file-validation';
import { LocalPreviews, createPreviewUrl, revokePreviewUrl } from '@shared/upload/image-preview';

const NAME_MAX = 30;
const DESCRIPTION_MAX = 30;
const LOCATION_MAX = 100;
const MAX_GALLERY = 5;
const MAX_MEMBERS = 100000;

type ImageSlot = 'icon' | 'banner';

@Component({
  selector: 'app-club-editor',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.Eager,
  templateUrl: './club-editor.component.html',
})
export class ClubEditorComponent implements OnInit, CanComponentDeactivate {
  private readonly fb = new FormBuilder();
  private readonly destroyRef = inject(DestroyRef);

  readonly clubTypes = ALL_CLUB_TYPES;
  readonly nameMax = NAME_MAX;
  readonly descriptionMax = DESCRIPTION_MAX;
  readonly locationMax = LOCATION_MAX;
  readonly maxMembers = MAX_MEMBERS;

  readonly form = this.fb.nonNullable.group({
    name: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(NAME_MAX)]),
    description: this.fb.nonNullable.control('', [
      Validators.required,
      Validators.maxLength(DESCRIPTION_MAX),
    ]),
    clubType: this.fb.nonNullable.control<ClubType>('Social', [Validators.required]),
    phone: this.fb.nonNullable.control('', [Validators.maxLength(30)]),
    email: this.fb.nonNullable.control('', [Validators.email]),
    location: this.fb.nonNullable.control('', [Validators.maxLength(LOCATION_MAX)]),
    websiteUrl: this.fb.nonNullable.control('', [
      Validators.pattern(/^https?:\/\/.+/i),
      Validators.maxLength(300),
    ]),
    maxMemberCount: this.fb.nonNullable.control<number>(1000, [
      Validators.required,
      Validators.min(0),
      Validators.max(MAX_MEMBERS),
    ]),
    isPrivate: this.fb.nonNullable.control(false),
  });

  isCreate = true;
  clubId = 0;
  imageUrl = '';
  imageUploading = false;
  dragActive = false;
  bannerUrl = '';
  bannerUploading = false;
  bannerDragActive = false;
  galleryUrls: string[] = [];
  galleryUploading = false;
  galleryDragActive = false;
  readonly maxGallery = MAX_GALLERY;
  readonly imageAccept = IMAGE_ACCEPT;
  /** Local previews of the picked icon and banner, shown until the next pick replaces them. */
  readonly slotPreviews: Record<ImageSlot, string | null> = { icon: null, banner: null };
  /** Local previews standing in for uploaded gallery photos, keyed by their public URL. */
  readonly galleryPreviews = new LocalPreviews();
  /** Previews of gallery photos still uploading. */
  pendingGalleryPreviews: string[] = [];
  /** One message per file the last gallery pick turned away. */
  galleryErrors: string[] = [];
  loading = false;
  saving = false;
  error = '';
  success = '';

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private management: ClubManagementService,
    private clubsService: ClubsService,
    private eventsManagement: EventsManagementService,
  ) {
    // Pending gallery previews are released by their upload's finalize, which destroy triggers.
    this.destroyRef.onDestroy(() => {
      this.setSlotPreview('icon', null);
      this.setSlotPreview('banner', null);
      this.galleryPreviews.releaseAll();
    });
  }

  ngOnInit(): void {
    const snap = this.route.snapshot;
    const clubId =
      Number.parseInt(snap.paramMap.get('clubId') ?? '', 10) ||
      Number.parseInt(snap.parent?.paramMap.get('clubId') ?? '', 10) ||
      0;

    this.isCreate = clubId <= 0;
    this.clubId = clubId;

    if (!this.isCreate) {
      this.loadClub();
    }
  }

  canDeactivate(): boolean {
    // A successful save marks the form pristine and clears imageDirty, so this
    // correctly allows navigation right after saving and prompts again once the
    // user makes further edits.
    if (!this.form.dirty && !this.imageDirty && !this.bannerDirty && !this.galleryDirty) {
      return true;
    }
    return window.confirm('You have unsaved changes. Leave without saving?');
  }

  private imageDirty = false;
  private bannerDirty = false;
  private galleryDirty = false;
  private galleryPending = 0;

  get canAddGallery(): boolean {
    return this.galleryUrls.length < MAX_GALLERY;
  }

  private loadClub(): void {
    this.loading = true;
    this.clubsService
      .getClub(this.clubId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.loading = false)),
      )
      .subscribe({
        next: (response) => {
          const club = response.data;
          if (!club) {
            this.error = response.message || 'Club not found.';
            return;
          }
          this.imageUrl = club.clubImage;
          this.bannerUrl = club.bannerImage ?? '';
          this.galleryUrls = [...(club.galleryImages ?? [])];
          this.form.patchValue({
            name: club.name,
            description: club.description,
            clubType: club.clubType,
            phone: club.phone ?? '',
            email: club.email ?? '',
            location: club.location ?? '',
            websiteUrl: club.websiteUrl ?? '',
            maxMemberCount: club.maxMemberCount || 1000,
            isPrivate: club.isPrivate,
          });
          this.form.markAsPristine();
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load this club.');
        },
      });
  }

  onDragOver(event: DragEvent, target: ImageSlot = 'icon'): void {
    event.preventDefault();
    if (target === 'banner') this.bannerDragActive = true;
    else this.dragActive = true;
  }

  onDragLeave(event: DragEvent, target: ImageSlot = 'icon'): void {
    event.preventDefault();
    if (target === 'banner') this.bannerDragActive = false;
    else this.dragActive = false;
  }

  async onDrop(event: DragEvent, target: ImageSlot = 'icon'): Promise<void> {
    event.preventDefault();
    if (target === 'banner') this.bannerDragActive = false;
    else this.dragActive = false;
    const file = event.dataTransfer?.files?.[0];
    if (file) await this.uploadFile(file, target);
  }

  async onImageSelected(event: Event, target: ImageSlot = 'icon'): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) await this.uploadFile(file, target);
  }

  removeBanner(): void {
    this.bannerUrl = '';
    this.setSlotPreview('banner', null);
    this.bannerDirty = true;
  }

  onGalleryDragOver(event: DragEvent): void {
    event.preventDefault();
    this.galleryDragActive = true;
  }

  onGalleryDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.galleryDragActive = false;
  }

  async onGalleryDrop(event: DragEvent): Promise<void> {
    event.preventDefault();
    this.galleryDragActive = false;
    if (event.dataTransfer?.files) await this.uploadGalleryFiles(event.dataTransfer.files);
  }

  async onGallerySelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = input.files;
    input.value = '';
    if (files) await this.uploadGalleryFiles(files);
  }

  removeGalleryImage(index: number): void {
    this.galleryPreviews.release(this.galleryUrls[index]);
    this.galleryUrls = this.galleryUrls.filter((_, i) => i !== index);
    this.galleryDirty = true;
  }

  private async uploadGalleryFiles(files: FileList): Promise<void> {
    this.error = '';
    this.success = '';
    this.galleryErrors = [];

    const remaining = MAX_GALLERY - this.galleryUrls.length;
    if (remaining <= 0) {
      this.error = `You can add up to ${MAX_GALLERY} photos.`;
      return;
    }

    // Screen the whole batch first, so every rejection is reported by name rather than the last
    // one overwriting the rest, and a rejected file does not take up one of the free slots.
    const accepted: File[] = [];
    for (const file of Array.from(files)) {
      const screened = await screenImageFile(file);
      if (screened.ok) accepted.push(file);
      else this.galleryErrors.push(screened.message);
    }
    for (const file of accepted.slice(remaining)) {
      this.galleryErrors.push(`"${file.name}" wasn't added — the gallery holds ${MAX_GALLERY}.`);
    }

    for (const file of accepted.slice(0, remaining)) {
      this.uploadGalleryFile(file);
    }
  }

  private uploadGalleryFile(file: File): void {
    const preview = createPreviewUrl(file);
    let adopted = false;
    if (preview) this.pendingGalleryPreviews = [...this.pendingGalleryPreviews, preview];

    this.galleryPending++;
    this.galleryUploading = true;
    this.eventsManagement
      .uploadImage(this.clubId, file)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.galleryPending--;
          this.galleryUploading = this.galleryPending > 0;
          this.pendingGalleryPreviews = this.pendingGalleryPreviews.filter((p) => p !== preview);
          if (!adopted) revokePreviewUrl(preview);
        }),
      )
      .subscribe({
        next: (publicUrl) => {
          if (this.galleryUrls.length < MAX_GALLERY) {
            this.galleryUrls = [...this.galleryUrls, publicUrl];
            this.galleryPreviews.adopt(publicUrl, preview);
            adopted = true;
            this.galleryDirty = true;
          }
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'The image upload failed.');
        },
      });
  }

  private async uploadFile(file: File, target: ImageSlot): Promise<void> {
    this.error = '';
    this.success = '';

    const screened = await screenImageFile(file);
    if (!screened.ok) {
      this.error = screened.message;
      return;
    }

    const setUploading = (value: boolean) =>
      target === 'banner' ? (this.bannerUploading = value) : (this.imageUploading = value);

    this.setSlotPreview(target, createPreviewUrl(file));
    setUploading(true);
    // clubId is 0 for a not-yet-created club; the backend issues a pending upload URL.
    this.eventsManagement
      .uploadImage(this.clubId, file)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => setUploading(false)),
      )
      .subscribe({
        next: (publicUrl) => {
          if (target === 'banner') {
            this.bannerUrl = publicUrl;
            this.bannerDirty = true;
          } else {
            this.imageUrl = publicUrl;
            this.imageDirty = true;
          }
        },
        error: (err) => {
          // Fall back to whatever image the slot really holds.
          this.setSlotPreview(target, null);
          this.error = getApiClientMessage(err, 'The image upload failed.');
        },
      });
  }

  private setSlotPreview(target: ImageSlot, url: string | null): void {
    revokePreviewUrl(this.slotPreviews[target]);
    this.slotPreviews[target] = url;
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    if (!this.imageUrl) {
      this.error = 'A club image is required.';
      return;
    }

    const {
      name,
      description,
      clubType,
      phone,
      email,
      location,
      websiteUrl,
      maxMemberCount,
      isPrivate,
    } = this.form.getRawValue();
    const payload = {
      name,
      description,
      clubtype: toClubtypeAlias(clubType),
      clubImageUrl: this.imageUrl,
      bannerImageUrl: this.bannerUrl || null,
      galleryImageUrls: this.galleryUrls,
      phone: phone || undefined,
      email: email || undefined,
      location: location.trim() || null,
      websiteUrl: websiteUrl.trim() || null,
      maxMemberCount,
      isPrivate,
    };

    this.saving = true;
    this.error = '';
    this.success = '';

    const request$ = this.isCreate
      ? this.management.createClub(payload)
      : this.management.updateClub(this.clubId, payload);

    request$
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.saving = false)),
      )
      .subscribe({
        next: (response) => {
          const club = response.data;
          this.imageDirty = false;
          this.bannerDirty = false;
          this.galleryDirty = false;
          this.form.markAsPristine();
          if (this.isCreate && club) {
            void this.router.navigate(['/clubs', club.id, 'manage']);
            return;
          }
          this.success = 'Club details saved.';
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to save the club.');
        },
      });
  }

  get nameControl() {
    return this.form.controls.name;
  }
  get descriptionControl() {
    return this.form.controls.description;
  }
  get emailControl() {
    return this.form.controls.email;
  }
  get websiteControl() {
    return this.form.controls.websiteUrl;
  }
  get maxMemberCountControl() {
    return this.form.controls.maxMemberCount;
  }
}
