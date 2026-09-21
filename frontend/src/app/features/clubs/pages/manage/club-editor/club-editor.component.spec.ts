import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Params, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';

import { bytesFile, envelope, imageFile, makeClub } from '@testing';

import { EventsManagementService } from '../../../../events/services/events-management.service';
import { Club } from '../../../models/club.types';
import { ClubManagementService } from '../../../services/club-management.service';
import { ClubsService } from '../../../services/clubs.service';
import { ClubEditorComponent } from './club-editor.component';

type ClubsStub = Pick<ClubsService, 'getClub'>;
type ManagementStub = Pick<ClubManagementService, 'createClub' | 'updateClub'>;
type UploadStub = Pick<EventsManagementService, 'uploadImage'>;

describe('ClubEditorComponent', () => {
  let fixture: ComponentFixture<ClubEditorComponent>;
  let component: ClubEditorComponent;
  let clubs: jasmine.SpyObj<ClubsStub>;
  let management: jasmine.SpyObj<ManagementStub>;
  let uploads: jasmine.SpyObj<UploadStub>;
  let router: Router;

  function setup(params: Params = {}, parentParams: Params | null = null): void {
    const route = {
      snapshot: {
        paramMap: convertToParamMap(params),
        parent: parentParams ? { paramMap: convertToParamMap(parentParams) } : null,
      },
    } as unknown as ActivatedRoute;

    TestBed.configureTestingModule({
      imports: [ClubEditorComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: route },
        { provide: ClubsService, useValue: clubs },
        { provide: ClubManagementService, useValue: management },
        { provide: EventsManagementService, useValue: uploads },
      ],
    });

    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    fixture = TestBed.createComponent(ClubEditorComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  const input = (files: File[] | null) => {
    const target = { files, value: 'selected' };
    return { event: { target } as unknown as Event, target };
  };

  const drag = (files: File[] | null) =>
    ({
      preventDefault: jasmine.createSpy('preventDefault'),
      dataTransfer: files ? { files } : null,
    }) as unknown as DragEvent;

  beforeEach(() => {
    clubs = jasmine.createSpyObj<ClubsStub>('ClubsService', ['getClub']);
    management = jasmine.createSpyObj<ManagementStub>('ClubManagementService', [
      'createClub',
      'updateClub',
    ]);
    uploads = jasmine.createSpyObj<UploadStub>('EventsManagementService', ['uploadImage']);
    clubs.getClub.and.returnValue(of(envelope(makeClub())));
  });

  describe('initialisation', () => {
    it('starts in create mode without a club id', () => {
      setup();

      expect(component.isCreate).toBeTrue();
      expect(component.clubId).toBe(0);
      expect(clubs.getClub).not.toHaveBeenCalled();
    });

    it('loads the club named by its own route', () => {
      clubs.getClub.and.returnValue(
        of(
          envelope(
            makeClub({
              id: 7,
              bannerImage: 'https://cdn/banner.png',
              galleryImages: ['https://cdn/g1.png'],
              phone: '555',
              email: 'a@b.c',
              location: 'Ottawa',
              websiteUrl: 'https://club.example',
              maxMemberCount: 40,
              isPrivate: true,
            }),
          ),
        ),
      );

      setup({ clubId: '7' });

      expect(clubs.getClub).toHaveBeenCalledOnceWith(7);
      expect(component.isCreate).toBeFalse();
      expect(component.bannerUrl).toBe('https://cdn/banner.png');
      expect(component.galleryUrls).toEqual(['https://cdn/g1.png']);
      expect(component.form.getRawValue()).toEqual(
        jasmine.objectContaining({
          phone: '555',
          email: 'a@b.c',
          location: 'Ottawa',
          websiteUrl: 'https://club.example',
          maxMemberCount: 40,
          isPrivate: true,
        }),
      );
      expect(component.form.pristine).toBeTrue();
      expect(component.loading).toBeFalse();
    });

    it('falls back to the parent route and defaults for missing fields', () => {
      const bare = {
        ...makeClub({ maxMemberCount: 0, location: null }),
        bannerImage: undefined,
        galleryImages: undefined,
      } as unknown as Club;
      clubs.getClub.and.returnValue(of(envelope(bare)));

      setup({}, { clubId: '9' });

      expect(clubs.getClub).toHaveBeenCalledOnceWith(9);
      expect(component.bannerUrl).toBe('');
      expect(component.galleryUrls).toEqual([]);
      expect(component.form.getRawValue()).toEqual(
        jasmine.objectContaining({ phone: '', email: '', location: '', maxMemberCount: 1000 }),
      );
    });

    it('reports a club that is not found', () => {
      clubs.getClub.and.returnValue(of(envelope<Club>(null, { message: 'Gone.' })));
      setup({ clubId: '7' });
      expect(component.error).toBe('Gone.');
    });

    it('uses a default message when a missing club has none', () => {
      clubs.getClub.and.returnValue(of(envelope<Club>(null, { message: '' })));
      setup({ clubId: '7' });
      expect(component.error).toBe('Club not found.');
    });

    it('reports a failed load', () => {
      clubs.getClub.and.returnValue(throwError(() => new Error('offline')));
      setup({ clubId: '7' });
      expect(component.error).toBe('offline');
    });
  });

  describe('canDeactivate', () => {
    beforeEach(() => setup());

    it('allows leaving a clean form', () => {
      expect(component.canDeactivate()).toBeTrue();
    });

    it('asks before discarding edits', () => {
      const confirm = spyOn(window, 'confirm').and.returnValue(false);
      component.form.markAsDirty();

      expect(component.canDeactivate()).toBeFalse();
      expect(confirm).toHaveBeenCalled();
    });

    it('asks before discarding a changed image', () => {
      spyOn(window, 'confirm').and.returnValue(true);
      component.removeBanner();

      expect(component.canDeactivate()).toBeTrue();
      expect(window.confirm).toHaveBeenCalled();
    });
  });

  describe('drag state', () => {
    beforeEach(() => setup());

    it('tracks the icon and banner drop zones separately', () => {
      component.onDragOver(drag(null));
      component.onDragOver(drag(null), 'banner');
      expect(component.dragActive).toBeTrue();
      expect(component.bannerDragActive).toBeTrue();

      component.onDragLeave(drag(null));
      component.onDragLeave(drag(null), 'banner');
      expect(component.dragActive).toBeFalse();
      expect(component.bannerDragActive).toBeFalse();
    });

    it('tracks the gallery drop zone', () => {
      component.onGalleryDragOver(drag(null));
      expect(component.galleryDragActive).toBeTrue();
      component.onGalleryDragLeave(drag(null));
      expect(component.galleryDragActive).toBeFalse();
    });
  });

  describe('icon and banner', () => {
    beforeEach(() => setup());

    it('ignores a drop or pick without a file', async () => {
      await component.onDrop(drag(null));
      await component.onDrop(drag([]), 'banner');
      const picked = input([]);
      await component.onImageSelected(picked.event);

      expect(picked.target.value).toBe('');
      expect(uploads.uploadImage).not.toHaveBeenCalled();
    });

    it('rejects an unsupported file locally, naming it', async () => {
      await component.onImageSelected(
        input([new File(['x'], 'logo.svg', { type: 'image/svg+xml' })]).event,
      );

      expect(component.error).toBe(
        `"logo.svg" isn't a supported image. Use JPG, PNG, WEBP or GIF.`,
      );
      expect(uploads.uploadImage).not.toHaveBeenCalled();
    });

    it('rejects a text file posing as a PNG', async () => {
      await component.onDrop(drag([bytesFile('fake.png', 'image/png', 'plain text')]));

      expect(component.error).toBe(`"fake.png" isn't a JPG, PNG, WEBP or GIF image.`);
      expect(uploads.uploadImage).not.toHaveBeenCalled();
    });

    it('previews the icon locally while it uploads, then keeps the preview', async () => {
      const upload = new Subject<string>();
      uploads.uploadImage.and.returnValue(upload);

      await component.onDrop(drag([await imageFile('icon.png')]));
      fixture.detectChanges();

      const img: HTMLImageElement = fixture.nativeElement.querySelector(
        'img[alt="Club image preview"]',
      );
      expect(img.src).toBe(component.slotPreviews.icon!);
      expect(component.imageUploading).toBeTrue();

      upload.next('https://cdn/icon.png');
      upload.complete();

      expect(uploads.uploadImage).toHaveBeenCalledWith(0, jasmine.any(File));
      expect(component.imageUrl).toBe('https://cdn/icon.png');
      expect(component.imageUploading).toBeFalse();
      expect(component.slotPreviews.icon).toMatch(/^blob:/);
    });

    it('uploads a banner and revokes its preview when removed', async () => {
      uploads.uploadImage.and.returnValue(of('https://cdn/banner.png'));
      const revoke = spyOn(URL, 'revokeObjectURL').and.callThrough();

      await component.onImageSelected(input([await imageFile('banner.png')]).event, 'banner');
      const preview = component.slotPreviews.banner;

      expect(component.bannerUrl).toBe('https://cdn/banner.png');
      expect(component.bannerUploading).toBeFalse();

      component.removeBanner();

      expect(revoke).toHaveBeenCalledOnceWith(preview!);
      expect(component.bannerUrl).toBe('');
      expect(component.slotPreviews.banner).toBeNull();
    });

    it('revokes the previous preview when the slot is re-picked', async () => {
      uploads.uploadImage.and.returnValue(of('https://cdn/icon.png'));
      const revoke = spyOn(URL, 'revokeObjectURL').and.callThrough();

      await component.onDrop(drag([await imageFile('one.png')]));
      const first = component.slotPreviews.icon;
      await component.onDrop(drag([await imageFile('two.png')]));

      expect(revoke).toHaveBeenCalledOnceWith(first!);
    });

    describe('overlapping picks for one slot', () => {
      let first: Subject<string>;
      let second: Subject<string>;

      beforeEach(() => {
        first = new Subject<string>();
        second = new Subject<string>();
        uploads.uploadImage.and.returnValues(first, second);
      });

      it('keeps the latest pick when the earlier upload finishes last', async () => {
        await component.onDrop(drag([await imageFile('a.png')]));
        await component.onDrop(drag([await imageFile('b.png')]));
        const latest = component.slotPreviews.icon;

        expect(component.imageUploading).toBeTrue();

        second.next('https://cdn/b.png');
        second.complete();
        first.next('https://cdn/a.png');
        first.complete();

        expect(component.imageUrl).toBe('https://cdn/b.png');
        expect(component.slotPreviews.icon).toBe(latest);
        expect(component.imageUploading).toBeFalse();
      });

      it('ignores a failure from the upload a newer pick replaced', async () => {
        await component.onDrop(drag([await imageFile('a.png')]), 'banner');
        await component.onDrop(drag([await imageFile('b.png')]), 'banner');
        const latest = component.slotPreviews.banner;
        const revoke = spyOn(URL, 'revokeObjectURL').and.callThrough();

        first.error(new Error('upload refused'));

        expect(component.slotPreviews.banner).toBe(latest);
        expect(component.bannerUploading).toBeTrue();
        expect(component.error).toBe('');
        expect(revoke).not.toHaveBeenCalled();

        second.next('https://cdn/b.png');
        second.complete();

        expect(component.bannerUrl).toBe('https://cdn/b.png');
        expect(component.bannerUploading).toBeFalse();
      });

      it('drops an earlier pick whose screening finishes after a newer one', async () => {
        const [a, b] = [await imageFile('a.png'), await imageFile('b.png')];

        await Promise.all([component.onDrop(drag([a])), component.onDrop(drag([b]))]);

        expect(uploads.uploadImage).toHaveBeenCalledOnceWith(0, b);
      });

      it('does not let a superseded rejection overwrite the latest pick', async () => {
        const svg = new File(['<svg/>'], 'logo.svg', { type: 'image/svg+xml' });
        const png = await imageFile('b.png');

        const stale = component.onDrop(drag([svg]));
        await component.onDrop(drag([png]));
        await stale;

        expect(component.error).toBe('');
        expect(uploads.uploadImage).toHaveBeenCalledOnceWith(0, png);
      });
    });

    it('drops the preview and reports a failed upload', async () => {
      uploads.uploadImage.and.returnValue(throwError(() => new Error('upload refused')));

      await component.onDrop(drag([await imageFile('icon.png')]), 'banner');

      expect(component.slotPreviews.banner).toBeNull();
      expect(component.bannerUploading).toBeFalse();
      expect(component.error).toBe('upload refused');
    });
  });

  describe('gallery', () => {
    beforeEach(() => setup());

    it('ignores a drop or pick without files', async () => {
      await component.onGalleryDrop(drag(null));
      await component.onGallerySelected(input(null).event);

      expect(uploads.uploadImage).not.toHaveBeenCalled();
    });

    it('refuses more photos once the gallery is full', async () => {
      component.galleryUrls = ['1', '2', '3', '4', '5'];

      await component.onGallerySelected(input([await imageFile('six.png')]).event);

      expect(component.error).toBe('You can add up to 5 photos.');
      expect(uploads.uploadImage).not.toHaveBeenCalled();
    });

    it('reports every rejected file in a mixed batch and uploads the rest', async () => {
      uploads.uploadImage.and.returnValue(of('https://cdn/ok.png'));
      const batch = [
        new File(['x'], 'logo.svg', { type: 'image/svg+xml' }),
        new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'huge.png', { type: 'image/png' }),
        await imageFile('ok.png'),
      ];

      await component.onGalleryDrop(drag(batch));

      expect(component.galleryErrors).toEqual([
        `"logo.svg" isn't a supported image. Use JPG, PNG, WEBP or GIF.`,
        '"huge.png" is larger than 5MB.',
      ]);
      expect(uploads.uploadImage).toHaveBeenCalledTimes(1);
      expect(component.galleryUrls).toEqual(['https://cdn/ok.png']);
      expect(component.galleryPreviews.srcFor('https://cdn/ok.png')).toMatch(/^blob:/);

      fixture.detectChanges();
      const alert: HTMLElement = fixture.nativeElement.querySelector('ul[role="alert"]');
      expect(alert.querySelectorAll('li').length).toBe(2);
    });

    it('names each valid file that does not fit', async () => {
      component.galleryUrls = ['1', '2', '3', '4'];
      uploads.uploadImage.and.returnValue(of('https://cdn/new.png'));

      await component.onGallerySelected(
        input([await imageFile('a.png'), await imageFile('b.png')]).event,
      );

      expect(uploads.uploadImage).toHaveBeenCalledTimes(1);
      expect(component.galleryErrors).toEqual([`"b.png" wasn't added — the gallery holds 5.`]);
    });

    it('shows a pending tile from the local file until the upload lands', async () => {
      const upload = new Subject<string>();
      uploads.uploadImage.and.returnValue(upload);

      await component.onGallerySelected(input([await imageFile('a.png')]).event);
      fixture.detectChanges();

      expect(component.pendingGalleryPreviews.length).toBe(1);
      expect(component.galleryUploading).toBeTrue();
      const tile: HTMLImageElement = fixture.nativeElement.querySelector('img[alt=""]');
      expect(tile.src).toBe(component.pendingGalleryPreviews[0]);

      upload.next('https://cdn/a.png');
      upload.complete();

      expect(component.pendingGalleryPreviews).toEqual([]);
      expect(component.galleryUploading).toBeFalse();
      expect(component.galleryUrls).toEqual(['https://cdn/a.png']);
    });

    it('discards an upload that lands after the gallery filled up', async () => {
      const upload = new Subject<string>();
      uploads.uploadImage.and.returnValue(upload);
      const revoke = spyOn(URL, 'revokeObjectURL').and.callThrough();

      await component.onGallerySelected(input([await imageFile('late.png')]).event);
      const preview = component.pendingGalleryPreviews[0];
      component.galleryUrls = ['1', '2', '3', '4', '5'];
      upload.next('https://cdn/late.png');
      upload.complete();

      expect(component.galleryUrls).not.toContain('https://cdn/late.png');
      expect(revoke).toHaveBeenCalledOnceWith(preview);
    });

    it('revokes the pending preview and reports a failed upload', async () => {
      uploads.uploadImage.and.returnValue(throwError(() => new Error('upload refused')));
      const revoke = spyOn(URL, 'revokeObjectURL').and.callThrough();

      await component.onGallerySelected(input([await imageFile('a.png')]).event);

      expect(revoke).toHaveBeenCalledTimes(1);
      expect(component.pendingGalleryPreviews).toEqual([]);
      expect(component.error).toBe('upload refused');
    });

    it('uploads without a pending tile where previews are unavailable', async () => {
      uploads.uploadImage.and.returnValue(of('https://cdn/a.png'));
      const file = await imageFile('a.png');
      const original = Object.getOwnPropertyDescriptor(URL, 'createObjectURL')!;
      Object.defineProperty(URL, 'createObjectURL', { value: undefined, configurable: true });

      try {
        await component.onGallerySelected(input([file]).event);
      } finally {
        Object.defineProperty(URL, 'createObjectURL', original);
      }

      expect(component.galleryUrls).toEqual(['https://cdn/a.png']);
      expect(component.galleryPreviews.srcFor('https://cdn/a.png')).toBe('https://cdn/a.png');
    });

    it('revokes a photo preview when the photo is removed', async () => {
      uploads.uploadImage.and.returnValue(of('https://cdn/a.png'));
      await component.onGallerySelected(input([await imageFile('a.png')]).event);
      const preview = component.galleryPreviews.srcFor('https://cdn/a.png');
      const revoke = spyOn(URL, 'revokeObjectURL').and.callThrough();

      component.removeGalleryImage(0);

      expect(revoke).toHaveBeenCalledOnceWith(preview);
      expect(component.galleryUrls).toEqual([]);
    });
  });

  it('revokes every preview on destroy', async () => {
    setup();
    uploads.uploadImage.and.returnValues(of('https://cdn/icon.png'), of('https://cdn/g.png'));
    await component.onDrop(drag([await imageFile('icon.png')]));
    await component.onGallerySelected(input([await imageFile('g.png')]).event);
    const revoke = spyOn(URL, 'revokeObjectURL').and.callThrough();

    fixture.destroy();

    expect(revoke).toHaveBeenCalledTimes(2);
  });

  describe('save', () => {
    const fill = () =>
      component.form.patchValue({ name: 'Chess', description: 'Knights', clubType: 'Social' });

    it('marks an invalid form touched instead of saving', () => {
      setup();

      component.save();

      expect(component.form.touched).toBeTrue();
      expect(management.createClub).not.toHaveBeenCalled();
    });

    it('requires a club image', () => {
      setup();
      fill();

      component.save();

      expect(component.error).toBe('A club image is required.');
    });

    it('creates a club with trimmed optional fields and opens it', () => {
      setup();
      fill();
      component.imageUrl = 'https://cdn/icon.png';
      component.form.patchValue({ location: '   ', websiteUrl: '' });
      management.createClub.and.returnValue(of(envelope(makeClub({ id: 42 }))));

      component.save();

      expect(management.createClub).toHaveBeenCalledOnceWith(
        jasmine.objectContaining({
          clubImageUrl: 'https://cdn/icon.png',
          bannerImageUrl: null,
          phone: undefined,
          email: undefined,
          location: null,
          websiteUrl: null,
        }),
      );
      expect(router.navigate).toHaveBeenCalledOnceWith(['/clubs', 42, 'manage']);
      expect(component.saving).toBeFalse();
    });

    it('reports success when a create returns no club', () => {
      setup();
      fill();
      component.imageUrl = 'https://cdn/icon.png';
      management.createClub.and.returnValue(of(envelope<Club>(null)));

      component.save();

      expect(router.navigate).not.toHaveBeenCalled();
      expect(component.success).toBe('Club details saved.');
    });

    it('updates an existing club with every field it holds', () => {
      setup({ clubId: '7' });
      component.bannerUrl = 'https://cdn/banner.png';
      component.form.patchValue({
        phone: '555',
        email: 'a@b.c',
        location: ' Ottawa ',
        websiteUrl: 'https://club.example ',
      });
      management.updateClub.and.returnValue(of(envelope(makeClub({ id: 7 }))));

      component.save();

      expect(management.updateClub).toHaveBeenCalledOnceWith(
        7,
        jasmine.objectContaining({
          bannerImageUrl: 'https://cdn/banner.png',
          phone: '555',
          email: 'a@b.c',
          location: 'Ottawa',
          websiteUrl: 'https://club.example',
        }),
      );
      expect(component.success).toBe('Club details saved.');
      expect(component.canDeactivate()).toBeTrue();
    });

    it('reports a failed save', () => {
      setup({ clubId: '7' });
      management.updateClub.and.returnValue(throwError(() => new Error('conflict')));

      component.save();

      expect(component.error).toBe('conflict');
      expect(component.saving).toBeFalse();
    });
  });

  it('exposes the form controls the template binds to', () => {
    setup();

    expect(component.nameControl).toBe(component.form.controls.name);
    expect(component.descriptionControl).toBe(component.form.controls.description);
    expect(component.emailControl).toBe(component.form.controls.email);
    expect(component.websiteControl).toBe(component.form.controls.websiteUrl);
    expect(component.maxMemberCountControl).toBe(component.form.controls.maxMemberCount);
  });
});
