import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Calendar, DelegationPerson, ParticipantList, Room, User } from '../../../core/api/models';
import {
  addMinutes,
  fromDateTimeInputs,
  toDateInput,
  toIso,
  toTimeInput,
} from '../../../core/time/date-utils';
import { recurrenceOptions } from '../../../core/time/recurrence-text';
import { AvailabilityGrid } from '../availability-grid/availability-grid';
import { TalkatonApi } from '../../../core/api/talkaton-api';

/** Что редактор отдаёт наружу. Страница сама решает, создать встречу или обновить. */
export interface EventDraft {
  calendarId: string;
  title: string;
  description: string | null;
  startUtc: string;
  endUtc: string;
  isAllDay: boolean;
  recurrenceRule: string | null;
  talkRoomSlug: string | null;
  participantIds: string[];
  reminderMinutesBefore: number;
  generateArtifacts?: boolean;
  roomId: string | null;
  /** От чьего имени создаём (Этап 7.6) — null значит «от своего»; применимо только при создании. */
  onBehalfOfUserId: string | null;
}

/** Начальное состояние формы: либо пустая встреча на выбранный слот, либо существующая. */
export interface EventEditorSeed {
  mode: 'create' | 'edit';
  eventId: string | null;
  calendarId: string;
  title: string;
  description: string;
  start: Date;
  end: Date;
  isAllDay: boolean;
  recurrenceRule: string | null;
  talkRoomSlug: string;
  participantIds: string[];
  reminderMinutesBefore: number;
  /** Есть ли у встречи запись/протокол — тумблер «Запись + ИИ-протокол» при правке. */
  hasArtifacts: boolean;
  roomId: string | null;
}

const REMINDER_CHOICES = [0, 5, 10, 15, 30];

/** Календарь → цветовая точка палитры. Обратное к COLOR_TO_CALENDAR. */
const CALENDAR_TO_COLOR: readonly (readonly [string, string])[] = [
  ['рождения', 'rose'],
  ['личное', 'teal'],
  ['задачи', 'amber'],
  ['рабочие', 'blue'],
];

/** Цветовая точка → календарь: клик по цвету переключает календарь встречи. */
const COLOR_TO_CALENDAR: Record<string, string> = {
  blue: 'рабочие',
  teal: 'личное',
  rose: 'рождения',
  amber: 'задачи',
};

/** «11:30» → 690. Кривой ввод не должен ронять грид занятости — тогда просто нет подсветки. */
function parseMinutesOfDay(time: string): number | null {
  const [hours, minutes] = time.split(':').map(Number);
  return Number.isFinite(hours) && Number.isFinite(minutes) ? hours * 60 + minutes : null;
}

/** Диалог «Создать встречу» и правки существующей. */
@Component({
  selector: 'app-event-editor',
  imports: [FormsModule, AvailabilityGrid],
  templateUrl: './event-editor.html',
  styleUrl: './event-editor.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EventEditor {
  private readonly api = inject(TalkatonApi);

  readonly seed = input.required<EventEditorSeed>();
  readonly calendars = input.required<readonly Calendar[]>();
  readonly people = input.required<readonly User[]>();
  readonly participantLists = input.required<readonly ParticipantList[]>();
  readonly rooms = input.required<readonly Room[]>();
  /** Этап 7.6: от чьего имени можно создавать встречи — те, кто выдал делегирование. */
  readonly grantedToMe = input<readonly DelegationPerson[]>([]);

  readonly saved = output<EventDraft>();
  readonly cancelled = output<void>();

  protected readonly reminderChoices = REMINDER_CHOICES;
  protected readonly eventColors: readonly string[] = ['blue', 'teal', 'purple', 'amber', 'rose', 'grey'];

  protected readonly title = linkedSignal(() => this.seed().title);
  protected readonly calendarId = linkedSignal(() => this.seed().calendarId);
  protected readonly description = linkedSignal(() => this.seed().description);
  protected readonly dateValue = linkedSignal(() => toDateInput(this.seed().start));
  protected readonly startTime = linkedSignal(() => toTimeInput(this.seed().start));
  protected readonly endTime = linkedSignal(() => toTimeInput(this.seed().end));
  protected readonly isAllDay = linkedSignal(() => this.seed().isAllDay);
  protected readonly recurrence = linkedSignal(() => this.seed().recurrenceRule);
  protected readonly talkRoomSlug = linkedSignal(() => this.seed().talkRoomSlug);
  protected readonly reminder = linkedSignal(() => this.seed().reminderMinutesBefore);
  protected readonly participants = linkedSignal(() => new Set(this.seed().participantIds));
  protected readonly roomId = linkedSignal(() => this.seed().roomId);
  protected readonly onBehalfOfUserId = linkedSignal<string | null>(() => null);

  // Тумблеры и палитра тоже читаются из seed: при правке они должны показывать
  // состояние самой встречи, а не дефолты формы создания.
  protected readonly selectedColor = linkedSignal(() => {
    const calendar = this.calendars().find((c) => c.id === this.seed().calendarId);
    const name = calendar?.name.toLowerCase() ?? '';
    return CALENDAR_TO_COLOR.find(([needle]) => name.includes(needle))?.[1] ?? 'blue';
  });
  protected readonly tolkVideo = linkedSignal(() =>
    this.seed().mode === 'create' ? true : this.seed().talkRoomSlug.trim().length > 0,
  );
  protected readonly recordAndAi = linkedSignal(() =>
    this.seed().mode === 'create' ? true : this.seed().hasArtifacts,
  );
  protected readonly repeatWeekly = linkedSignal(() => this.seed().recurrenceRule !== null);

  protected readonly validationError = signal<string | null>(null);

  protected readonly heading = computed(() =>
    this.seed().mode === 'create' ? 'Новая встреча' : 'Изменить встречу',
  );

  /** Пресеты повторения зависят от дня недели: «каждую среду» считается от даты начала. */
  protected readonly recurrenceChoices = computed(() =>
    recurrenceOptions(fromDateTimeInputs(this.dateValue(), this.startTime()) ?? this.seed().start),
  );

  protected readonly chosenPeople = computed(() => {
    const chosen = this.participants();
    return this.people().filter((p) => chosen.has(p.id));
  });

  /** День для грида занятости (Этап 7.1) — тот же, что выбран в поле «Дата». */
  protected readonly gridDay = computed(() => fromDateTimeInputs(this.dateValue(), '00:00') ?? this.seed().start);
  protected readonly rangeStartMinutes = computed(() => parseMinutesOfDay(this.startTime()));
  protected readonly rangeEndMinutes = computed(() => parseMinutesOfDay(this.endTime()));

  protected reminderLabel(minutes: number): string {
    return minutes === 0 ? 'Не напоминать' : `За ${minutes} мин`;
  }

  protected isChosen(userId: string): boolean {
    return this.participants().has(userId);
  }

  protected toggleParticipant(userId: string): void {
    this.participants.update((chosen) => {
      const next = new Set(chosen);
      if (!next.delete(userId)) {
        next.add(userId);
      }

      return next;
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

  protected selectColor(color: string): void {
    this.selectedColor.set(color);
    // Синхронизируем календарь при клике на цвет
    const needle = COLOR_TO_CALENDAR[color];
    if (needle) {
      const cal = this.calendars().find((c) => c.name.toLowerCase().includes(needle));
      if (cal) {
        this.calendarId.set(cal.id);
      }
    }
  }

  protected toggleTolkVideo(on: boolean): void {
    this.tolkVideo.set(on);
    if (on && !this.talkRoomSlug().trim()) {
      this.talkRoomSlug.set('room-' + Math.random().toString(36).slice(2, 8));
    } else if (!on) {
      this.talkRoomSlug.set('');
    }
  }

  protected toggleRepeatWeekly(on: boolean): void {
    this.repeatWeekly.set(on);
    if (on) {
      const choices = this.recurrenceChoices();
      const weekly = choices.find((c) => c.value !== null);
      if (weekly) {
        this.recurrence.set(weekly.value);
      }
    } else {
      this.recurrence.set(null);
    }
  }

  protected isListFullyAdded(list: ParticipantList): boolean {
    if (list.members.length === 0) return false;
    const chosen = this.participants();
    return list.members.every((m) => chosen.has(m.id));
  }

  protected readonly roundRobinPick = signal<string | null>(null);

  /**
   * Ротация по очереди (Этап 7.3): вместо приглашения всех из списка добавляем только
   * одного — того, чья очередь по кругу. Бэкенд сам сдвигает курсор ротации.
   */
  protected pickRoundRobin(list: ParticipantList, event: MouseEvent): void {
    event.stopPropagation();
    this.api.nextRoundRobinMember(list.id).subscribe({
      next: (person) => {
        this.participants.update((chosen) => new Set(chosen).add(person.id));
        this.roundRobinPick.set(`По очереди назначен: ${person.displayName}`);
      },
      error: () => this.roundRobinPick.set('Не удалось назначить по очереди — список пуст?'),
    });
  }

  protected toggleList(list: ParticipantList): void {
    const isAdded = this.isListFullyAdded(list);
    this.participants.update((chosen) => {
      const next = new Set(chosen);
      for (const member of list.members) {
        if (isAdded) {
          next.delete(member.id);
        } else {
          next.add(member.id);
        }
      }
      return next;
    });
  }

  protected submit(): void {
    const title = this.title().trim();
    if (title.length === 0) {
      this.validationError.set('Укажите тему встречи');
      return;
    }

    const allDay = this.isAllDay();
    const start = allDay
      ? fromDateTimeInputs(this.dateValue(), '00:00')
      : fromDateTimeInputs(this.dateValue(), this.startTime());
    const end = allDay
      ? fromDateTimeInputs(this.dateValue(), '23:59')
      : fromDateTimeInputs(this.dateValue(), this.endTime());

    if (!start || !end) {
      this.validationError.set('Проверьте дату и время');
      return;
    }

    // Конец раньше начала обычно значит встречу через полночь — переносим на следующий день.
    const finish = end <= start ? addMinutes(end, 24 * 60) : end;

    this.validationError.set(null);
    this.saved.emit({
      calendarId: this.calendarId(),
      title,
      description: this.description().trim() || null,
      startUtc: toIso(start),
      endUtc: toIso(finish),
      isAllDay: allDay,
      recurrenceRule: this.recurrence(),
      talkRoomSlug: this.talkRoomSlug().trim() || null,
      participantIds: [...this.participants()],
      reminderMinutesBefore: this.reminder(),
      generateArtifacts: this.recordAndAi(),
      roomId: this.roomId(),
      onBehalfOfUserId: this.onBehalfOfUserId(),
    });
  }
}
