// AppComponent smoke - keeps Angular CLI's auto-scaffolded test green
// after the SP1-09 shell rewrite. Frontend test breadth deferred to
// SP-21; this file stays only so `ng test` infrastructure remains wired.
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AppComponent } from './app.component';

describe('AppComponent', () => {
  beforeEach(async () => {
    // provideRouter is required because RouterLink / RouterLinkActive
    // in the template inject Router; without it TestBed throws
    // NullInjectorError. Empty routes are enough for a render smoke.
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('should create the app shell', () => {
    const fixture = TestBed.createComponent(AppComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders the four nav links for SP1-09 routes', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    const linkLabels = Array.from(compiled.querySelectorAll('nav a')).map(
      (a) => a.textContent?.trim(),
    );
    expect(linkLabels).toEqual(['Landing', 'Live', 'Analytics', 'Ops']);
  });
});
