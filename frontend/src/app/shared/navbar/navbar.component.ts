import { CommonModule } from '@angular/common';
import { Component, HostListener } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { Observable } from 'rxjs';
import { selectUser } from '../../core/stores/user.selectors';
import { User } from '../../core/stores/user.model';
import { UserState } from '../../core/stores/user.reducer';
import { clearUser } from '../../core/stores/user.actions';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './navbar.component.html',
})
export class NavbarComponent {
  scrolled = false;
  customersOpen = false;
  userMenuOpen = false;
  mobileOpen = false;
  user$: Observable<User | null>;
  isCollapsed = true;

  constructor(private store: Store<{ user: UserState }>) {
    this.user$ = this.store.select(selectUser);
  }

  @HostListener('window:scroll')
  onScroll() {
    this.scrolled = window.scrollY > 40;
  }

  toggleMobile() {
    this.mobileOpen = !this.mobileOpen;
  }

  toggleCustomers() {
    this.customersOpen = !this.customersOpen;
    this.userMenuOpen = false;
  }

  toggleUserMenu() {
    this.userMenuOpen = !this.userMenuOpen;
    this.customersOpen = false;
  }

  closeAllDropdowns() {
    this.customersOpen = false;
    this.userMenuOpen = false;
  }

  logout() {
    this.store.dispatch(clearUser());
  }
}
