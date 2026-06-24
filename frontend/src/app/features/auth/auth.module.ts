import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';

import { ChangePasswordComponent } from './pages/change-password/change-password.component';
import { DeviceVerifyComponent } from './pages/device-verify/device-verify.component';
import { ForgotPasswordComponent } from './pages/forgot-password/forgot-password.component';
import { GoogleCallbackComponent } from './pages/google-callback/google-callback.component';
import { LoginComponent } from './pages/login/login.component';
import { MicrosoftCallbackComponent } from './pages/microsoft-callback/microsoft-callback.component';
import { OAuthRoleComponent } from './pages/oauth-role/oauth-role.component';
import { SignupComponent } from './pages/signup/signup.component';
import { StepUpVerifyComponent } from './pages/step-up-verify/step-up-verify.component';
import { VerifyComponent } from './pages/verify/verify.component';

@NgModule({
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterModule.forChild([
      { path: '', redirectTo: 'login', pathMatch: 'full' },
      { path: 'login', component: LoginComponent },
      { path: 'signup', component: SignupComponent },
      { path: 'register', redirectTo: 'signup', pathMatch: 'full' },
      { path: 'forgot-password', component: ForgotPasswordComponent },
      { path: 'change-password', component: ChangePasswordComponent },
      { path: 'verify', component: VerifyComponent },
      { path: 'device/verify', component: DeviceVerifyComponent },
      { path: 'mfa', component: StepUpVerifyComponent },
      { path: 'oauth/role', component: OAuthRoleComponent },
      { path: 'google', component: GoogleCallbackComponent },
      { path: 'microsoft', component: MicrosoftCallbackComponent },
    ]),
  ],
})
export class AuthModule {}
