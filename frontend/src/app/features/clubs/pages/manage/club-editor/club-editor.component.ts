import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
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

const MAX_IMAGE_BYTES = 5 * 1024 * 1024;
const NAME_MAX = 30;
const DESCRIPTION_MAX = 30;

@Component({
  selector: 'app-club-editor',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './club-editor.component.html',
})
export class ClubEditorComponent implements OnInit, CanComponentDeactivate {
  private readonly fb = new FormBuilder();
  private readonly destroyRef = inject(DestroyRef);

  readonly clubTypes = ALL_CLUB_TYPES;
  readonly nameMax = NAME_MAX;
  readonly descriptionMax = DESCRIPTION_MAX;

  readonly form = this.fb.nonNullable.group({
    name: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(NAME_MAX)]),
    description: this.fb.nonNullable.control('', [
      Validators.required,
      Validators.maxLength(DESCRIPTION_MAX),
    ]),
    clubType: this.fb.nonNullable.control<ClubType>('Social', [Validators.required]),
    phone: this.fb.nonNullable.control('', [Validators.maxLength(30)]),
    email: this.fb.nonNullable.control('', [Validators.email]),
  });

  isCreate = true;
  clubId = 0;
  imageUrl = '';
  imageUploading = false;
  dragActive = false;
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
  ) {}

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
    if (!this.form.dirty && !this.imageDirty) {
      return true;
    }
    return window.confirm('You have unsaved changes. Leave without saving?');
  }

  private imageDirty = false;

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
          this.form.patchValue({
            name: club.name,
            description: club.description,
            clubType: club.clubType,
            phone: club.phone ?? '',
            email: club.email ?? '',
          });
          this.form.markAsPristine();
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'Unable to load this club.');
        },
      });
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragActive = true;
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragActive = false;
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragActive = false;
    const file = event.dataTransfer?.files?.[0];
    if (file) this.uploadFile(file);
  }

  onImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) this.uploadFile(file);
  }

  private uploadFile(file: File): void {
    this.error = '';
    this.success = '';

    if (!file.type.startsWith('image/')) {
      this.error = 'Please choose an image file.';
      return;
    }
    if (file.size > MAX_IMAGE_BYTES) {
      this.error = 'Image must be smaller than 5MB.';
      return;
    }

    this.imageUploading = true;
    // clubId is 0 for a not-yet-created club; the backend issues a pending upload URL.
    this.eventsManagement
      .uploadImage(this.clubId, file)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => (this.imageUploading = false)),
      )
      .subscribe({
        next: (publicUrl) => {
          this.imageUrl = publicUrl;
          this.imageDirty = true;
        },
        error: (err) => {
          this.error = getApiClientMessage(err, 'The image upload failed.');
        },
      });
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

    const { name, description, clubType, phone, email } = this.form.getRawValue();
    const payload = {
      name,
      description,
      clubtype: toClubtypeAlias(clubType),
      clubImageUrl: this.imageUrl,
      phone: phone || undefined,
      email: email || undefined,
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
}
