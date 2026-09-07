import { Routes } from '@angular/router';
import { signedInGuard } from './core/session/session.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'calendar',
    canActivate: [signedInGuard],
    loadComponent: () => import('./features/calendar/calendar-page').then((m) => m.CalendarPage),
  },
  {
    // Публичная страница самозаписи (Этап 7.4): «забронировать время у меня».
    // Требует входа (своих внешних гостей у нас нет, см. README про вход по имени),
    // но не требует, чтобы бронирующий был как-то связан с хозяином ссылки заранее.
    path: 'book/:userId',
    canActivate: [signedInGuard],
    loadComponent: () => import('./features/booking/booking-page').then((m) => m.BookingPage),
  },
  { path: '', pathMatch: 'full', redirectTo: 'calendar' },
  { path: '**', redirectTo: 'calendar' },
];
