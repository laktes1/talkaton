import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { TalkatonApi } from '../../../core/api/talkaton-api';
import { User, UserAvailability } from '../../../core/api/models';
import { addDays, startOfDay, toIso } from '../../../core/time/date-utils';

const DISPLAY_START_HOUR = 7;
const DISPLAY_END_HOUR = 21;
const DISPLAY_MINUTES = (DISPLAY_END_HOUR - DISPLAY_START_HOUR) * 60;

interface Segment {
  leftPct: number;
  widthPct: number;
}

interface PersonRow {
  user: User;
  segments: Segment[];
}

/**
 * Грид занятости участников (Этап 7.1) — по мотивам Google Find a Time / Outlook Scheduling
 * Assistant: кто из приглашённых занят, а кто свободен на выбранный день. Отдаёт только
 * «занято/свободно», без темы чужой встречи — так же, как API `/api/availability`.
 */
@Component({
  selector: 'app-availability-grid',
  templateUrl: './availability-grid.html',
  styleUrl: './availability-grid.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AvailabilityGrid {
  private readonly api = inject(TalkatonApi);

  readonly people = input.required<readonly User[]>();
  readonly day = input.required<Date>();
  /** Минуты от полуночи локального дня — граница выбранного времени встречи. */
  readonly rangeStartMinutes = input<number | null>(null);
  readonly rangeEndMinutes = input<number | null>(null);

  protected readonly hourMarks = [7, 10, 13, 16, 19];
  protected readonly displayStartHour = DISPLAY_START_HOUR;
  protected readonly displayEndHour = DISPLAY_END_HOUR;

  private readonly availabilityState = signal<ReadonlyMap<string, UserAvailability>>(new Map());
  protected readonly loading = signal(false);

  protected readonly selectionStyle = computed<Segment | null>(() => {
    const start = this.rangeStartMinutes();
    const end = this.rangeEndMinutes();
    return start === null || end === null ? null : this.toSegment(start, end);
  });

  protected readonly rows = computed<PersonRow[]>(() => {
    const availability = this.availabilityState();
    return this.people().map((user) => {
      const entry = availability.get(user.id);
      const segments = (entry?.busyBlocks ?? []).map((block) =>
        this.toSegment(
          this.minutesSinceDayStart(new Date(block.startUtc)),
          this.minutesSinceDayStart(new Date(block.endUtc)),
        ),
      );

      return { user, segments };
    });
  });

  constructor() {
    // Перезапрашиваем занятость при смене состава участников или выбранного дня —
    // ручного вызова не нужно, форма и так живёт на signals.
    effect(() => {
      const ids = this.people().map((p) => p.id);
      const day = this.day();

      if (ids.length === 0) {
        this.availabilityState.set(new Map());
        return;
      }

      const from = startOfDay(day);
      const to = addDays(from, 1);
      this.loading.set(true);

      this.api.availability(ids, toIso(from), toIso(to)).subscribe({
        next: (result) => {
          this.availabilityState.set(new Map(result.map((x) => [x.userId, x])));
          this.loading.set(false);
        },
        error: () => {
          // Грид — вспомогательная подсказка, не должен ронять форму создания встречи.
          this.availabilityState.set(new Map());
          this.loading.set(false);
        },
      });
    });
  }

  protected initials(name: string): string {
    return name
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part.charAt(0).toUpperCase())
      .join('');
  }

  private minutesSinceDayStart(instant: Date): number {
    return Math.round((instant.getTime() - startOfDay(this.day()).getTime()) / 60_000);
  }

  /** Минуты дня → проценты внутри отображаемого окна 07:00–21:00, с обрезкой по краям. */
  private toSegment(startMinutes: number, endMinutes: number): Segment {
    const windowStart = DISPLAY_START_HOUR * 60;
    const windowEnd = DISPLAY_END_HOUR * 60;
    const clampedStart = Math.min(Math.max(startMinutes, windowStart), windowEnd);
    const clampedEnd = Math.min(Math.max(endMinutes, windowStart), windowEnd);

    return {
      leftPct: ((clampedStart - windowStart) / DISPLAY_MINUTES) * 100,
      widthPct: Math.max(((clampedEnd - clampedStart) / DISPLAY_MINUTES) * 100, 0.5),
    };
  }
}
