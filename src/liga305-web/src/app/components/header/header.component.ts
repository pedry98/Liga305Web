import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './header.component.html',
  styleUrl: './header.component.scss'
})
export class HeaderComponent {
  private readonly auth = inject(AuthService);

  readonly user = this.auth.user;
  readonly isReady = this.auth.isReady;

  steamLoginUrl = this.auth.loginUrl(window.location.pathname || '/');

  async logout() {
    await this.auth.logout();
    window.location.href = '/';
  }
}
