import { Component, inject, OnInit, PLATFORM_ID } from '@angular/core';
import { isPlatformBrowser, NgIf } from '@angular/common';
import { RouterOutlet } from '@angular/router';
import { SessionManagerService } from './core/services/session-manager.service';
import { NavbarComponent } from './shared/navbar/navbar.component';
import { FooterComponent } from './shared/footer/footer.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, NgIf, NavbarComponent, FooterComponent],
  templateUrl: './app.html',
})
export class App implements OnInit {
  private session = inject(SessionManagerService);
  private platformId = inject(PLATFORM_ID);
  protected readonly title = 'frontend';

  ngOnInit(): void {
    if (isPlatformBrowser(this.platformId)) {
      this.session.restoreSession();
    }
  }

  get loading() {
    return this.session.loading();
  }
}
