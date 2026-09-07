import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { SessionService } from '../../core/session/session.service';
import { ReminderService } from '../../features/reminders/reminder.service';

interface NavTab {
  readonly label: string;
  readonly icon: 'calendar' | 'chat' | 'folder' | 'board' | 'contacts';
  /** Пока активен только «Календарь» — остальные вкладки живут в самом Толке. */
  readonly active: boolean;
  readonly badge?: number;
}

@Component({
  selector: 'app-header',
  templateUrl: './app-header.html',
  styleUrl: './app-header.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppHeader {
  private readonly session = inject(SessionService);
  private readonly reminders = inject(ReminderService);
  private readonly router = inject(Router);

  protected readonly tabs: readonly NavTab[] = [
    { label: 'Календарь', icon: 'calendar', active: true },
    { label: 'Чаты', icon: 'chat', active: false, badge: 1 },
    { label: 'Артефакты', icon: 'folder', active: false },
    { label: 'Доски', icon: 'board', active: false },
    { label: 'Контакты', icon: 'contacts', active: false },
  ];

  protected readonly user = this.session.user;

  /** Кнопку показываем, только пока разрешение ещё не спрошено: жест человека обязателен. */
  protected readonly canAskForNotifications = computed(
    () => this.session.isSignedIn() && this.reminders.permission() === 'default',
  );

  protected readonly initials = computed(() => {
    const name = this.user()?.displayName ?? '';
    return name
      .split(' ')
      .filter((part) => part.length > 0)
      .slice(0, 2)
      .map((part) => part[0].toUpperCase())
      .join('');
  });

  protected readonly linkCopied = signal(false);
  private copiedTimer: ReturnType<typeof setTimeout> | null = null;

  protected enableNotifications(): void {
    this.reminders.requestPermission();
  }

  /**
   * Публичная страница самозаписи (Этап 7.4) — ссылка на самого себя, чтобы коллеги
   * сами бронировали время, глядя на занятость, вместо переписки «когда вам удобно».
   */
  protected copyBookingLink(): void {
    const id = this.user()?.id;
    if (!id) {
      return;
    }

    const url = `${location.origin}/book/${id}`;
    navigator.clipboard?.writeText(url).then(
      () => {
        this.linkCopied.set(true);
        if (this.copiedTimer) {
          clearTimeout(this.copiedTimer);
        }
        this.copiedTimer = setTimeout(() => this.linkCopied.set(false), 2000);
      },
      () => {
        // Буфер обмена недоступен (нет разрешения/не https) — тихо ничего не делаем,
        // это вспомогательная кнопка, а не критичная функциональность.
      },
    );
  }

  protected signOut(): void {
    this.session.signOut();
    void this.router.navigate(['/login']);
  }
}
