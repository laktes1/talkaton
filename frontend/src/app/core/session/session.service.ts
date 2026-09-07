import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { TalkatonApi } from '../api/talkaton-api';
import { User } from '../api/models';

const STORAGE_KEY = 'talkaton.user';

/**
 * Вход по имени из плана 2.1. Паролей нет: имя — это и логин, и ключ к своим данным.
 * Кто вошёл, помним в localStorage, чтобы перезагрузка страницы не выкидывала на экран входа.
 * На этапе 6 весь файл заменяется на SSO Контура.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly api = inject(TalkatonApi);
  private readonly current = signal<User | null>(readStored());

  readonly user = this.current.asReadonly();
  readonly isSignedIn = computed(() => this.current() !== null);

  signIn(name: string): Observable<User> {
    return this.api.signIn(name).pipe(tap((user) => this.remember(user)));
  }

  signOut(): void {
    this.current.set(null);
    write(null);
  }

  /** Профиль поменялся уже после входа (например, буфер до/после встречи, Этап 7.5). */
  updateUser(user: User): void {
    this.remember(user);
  }

  private remember(user: User): void {
    this.current.set(user);
    write(user);
  }
}

/**
 * Хранилище бывает недоступно: приватный режим, запрет на сайты, тестовое окружение.
 * Календарь от этого работать не перестаёт — просто спросит имя ещё раз.
 */
function readStored(): User | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return null;
    }

    const parsed = JSON.parse(raw) as User;
    return typeof parsed?.id === 'string' ? parsed : null;
  } catch {
    return null;
  }
}

function write(user: User | null): void {
  try {
    if (user) {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(user));
    } else {
      localStorage.removeItem(STORAGE_KEY);
    }
  } catch {
    // Не сохранилось — в этой вкладке вход всё равно работает.
  }
}
