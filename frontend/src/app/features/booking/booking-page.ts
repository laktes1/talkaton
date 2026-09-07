import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { AvailabilityGrid } from '../calendar/availability-grid/availability-grid';
import { TalkatonApi } from '../../core/api/talkaton-api';
import { Calendar, User } from '../../core/api/models';
import { addMinutes, fromDateTimeInputs, toDateInput, toIso } from '../../core/time/date-utils';

const DURATION_CHOICES = [15, 30, 45, 60, 90];

/**
 * Публичная страница самозаписи (Этап 7.4) — по мотивам Calendly/Планёрки: приглашённый
 * (уже вошедший в Толкатрон коллега — своих внешних гостей у нас нет, см. README про
 * вход по имени) открывает ссылку на конкретного человека, видит его занятость и сам
 * бронирует слот, без переписки «когда вам удобно».
 */
@Component({
  selector: 'app-booking-page',
  imports: [FormsModule, AvailabilityGrid],
  templateUrl: './booking-page.html',
  styleUrl: './booking-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BookingPage {
  private readonly api = inject(TalkatonApi);
  private readonly route = inject(ActivatedRoute);

  private readonly userId = this.route.snapshot.paramMap.get('userId') ?? '';

  protected readonly host = signal<User | null>(null);
  protected readonly myCalendars = signal<Calendar[]>([]);
  protected readonly loadError = signal<string | null>(null);

  protected readonly durationChoices = DURATION_CHOICES;

  private readonly today = new Date();
  protected readonly dateValue = signal(toDateInput(this.today));
  protected readonly startTime = signal('10:00');
  protected readonly durationMinutes = linkedSignal(() => this.durationChoices[1]);

  protected readonly day = computed(() => fromDateTimeInputs(this.dateValue(), '00:00') ?? this.today);
  protected readonly hostAsList = computed(() => (this.host() ? [this.host()!] : []));
  protected readonly rangeStartMinutes = computed(() => parseMinutesOfDay(this.startTime()));
  protected readonly rangeEndMinutes = computed(() => {
    const start = this.rangeStartMinutes();
    return start === null ? null : start + this.durationMinutes();
  });

  protected readonly submitting = signal(false);
  protected readonly submitError = signal<string | null>(null);
  protected readonly bookedOk = signal(false);

  constructor() {
    if (this.userId.length === 0) {
      this.loadError.set('Ссылка неполная — не указан человек, к которому бронируем время.');
      return;
    }

    this.api.user(this.userId).subscribe({
      next: (user) => this.host.set(user),
      error: () => this.loadError.set('Такого человека не нашли — ссылка могла устареть.'),
    });

    this.api.calendars().subscribe({
      next: (calendars) => this.myCalendars.set(calendars),
      error: () => this.loadError.set('Не удалось загрузить ваши календари.'),
    });
  }

  protected book(): void {
    const host = this.host();
    const calendar = this.myCalendars()[0];
    const start = fromDateTimeInputs(this.dateValue(), this.startTime());

    if (!host || !calendar || !start) {
      this.submitError.set('Проверьте дату и время.');
      return;
    }

    const end = addMinutes(start, this.durationMinutes());
    this.submitting.set(true);
    this.submitError.set(null);

    this.api
      .createEvent({
        calendarId: calendar.id,
        title: `Встреча с ${host.displayName}`,
        startUtc: toIso(start),
        endUtc: toIso(end),
        isAllDay: false,
        participantIds: [host.id],
      })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.bookedOk.set(true);
        },
        error: (err: HttpErrorResponse) => {
          this.submitting.set(false);
          this.submitError.set(err.error?.detail ?? 'Не удалось забронировать время — попробуйте другой слот.');
        },
      });
  }
}

function parseMinutesOfDay(time: string): number | null {
  const [hours, minutes] = time.split(':').map(Number);
  return Number.isFinite(hours) && Number.isFinite(minutes) ? hours * 60 + minutes : null;
}
