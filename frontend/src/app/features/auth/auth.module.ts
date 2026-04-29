import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';

import { LoginComponent } from './pages/login/login.component';
import { GoogleCallbackComponent } from './pages/google-callback/google-callback.component';
import { MicrosoftCallbackComponent } from './pages/microsoft-callback/microsoft-callback.component';
import { OAuthRoleComponent } from './pages/oauth-role/oauth-role.component';
import { VerifyComponent } from './pages/verify/verify.component';
import { DeviceVerifyComponent } from './pages/device-verify/device-verify.component';

@NgModule({
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterModule.forChild([
      { path: '', redirectTo: 'login', pathMatch: 'full' },
      { path: 'login', component: LoginComponent },
      { path: 'verify', component: VerifyComponent },
      { path: 'device/verify', component: DeviceVerifyComponent },
      { path: 'oauth/role', component: OAuthRoleComponent },
      { path: 'google', component: GoogleCallbackComponent },
      { path: 'microsoft', component: MicrosoftCallbackComponent },
    ]),
  ],
})
export class AuthModule {}
