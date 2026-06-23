// Root shell - thin host for the top nav + router-outlet. The 4 routes
// are lazy-loaded so this component itself stays tiny. SP1-11 will
// replace the unstyled nav with a real header/footer; for SP1-09 the
// minimum that makes routes navigable.
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppComponent {}
