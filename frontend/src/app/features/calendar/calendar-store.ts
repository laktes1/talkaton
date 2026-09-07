import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TalkatonApi } from '../../core/api/talkaton-api';
import {
  Calendar,
  CreateEventRequest,
  DelegationPerson,
  EditScope,
  EventDetails,
  Occurrence,
  ParticipantList,
  ParticipantStatus,
  Room,
  UpdateEventRequest,
} from '../../core/api/models';
import {
  addDays,
  addMonths,
  formatFullDate,
  formatMonthYear,
  formatWeekRange,
  startOfDay,
  startOfMonth,
  startOfWeek,
  startOfYear,
  toIso,
} from '../../core/time/date-utils';

export type CalendarView = 'day' | 'week' | 'month' | 'year';

/** Месячная сетка всегда шесть недель — иначе строки прыгают от месяца к месяцу. */
export const MONTH_GRID_WEEKS = 6;

/** Ссылка на конкретное вхождение: id серии плюс ключ вхождения внутри неё. */
export interface OccurrenceKey {
  eventId: string;
  occurrenceStartUtc: string;
}

export interface DateRange {
  from: Date;
  to: Date;
}

/**
 * Состояние раздела «Календарь»: что показываем, за какой период и что выбрано.
 * Живёт на уровне страницы, а не приложения — уход в другой раздел должен обнулять выбор.
 */
export type BirthdayScope = 'all' | 'dept' | 'fav';

@Injectable()
export class CalendarStore {
  private readonly api = inject(TalkatonApi);
  private readonly destroyRef = inject(DestroyRef);

  private readonly viewState = signal<CalendarView>('week');
  private readonly anchorState = signal(startOfDay(new Date()));
  private readonly calendarsState = signal<Calendar[]>([]);
  private readonly participantListsState = signal<ParticipantList[]>([]);
  private readonly roomsState = signal<Room[]>([]);
  private readonly myDelegatesState = signal<DelegationPerson[]>([]);
  private readonly grantedToMeState = signal<DelegationPerson[]>([]);
  private readonly delegationSavingState = signal(false);
  private readonly delegationErrorState = signal<string | null>(null);
  private readonly occurrencesState = signal<Occurrence[]>([]);
  private readonly selectedState = signal<OccurrenceKey | null>(null);
  private readonly detailsState = signal<EventDetails | null>(null);
  private readonly searchState = signal('');
  private readonly loadingState = signal(false);
  private readonly errorState = signal<string | null>(null);
  private readonly birthdayScopeState = signal<BirthdayScope>('dept');
  private readonly toastMessageState = signal<string | null>(null);
  private toastTimer: ReturnType<typeof setTimeout> | null = null;

  readonly view = this.viewState.asReadonly();

  /** Опорная дата: выбранный день, от которого считается видимый период. */
  readonly anchor = this.anchorState.asReadonly();
  readonly calendars = this.calendarsState.asReadonly();
  readonly participantLists = this.participantListsState.asReadonly();
  readonly rooms = this.roomsState.asReadonly();
  readonly myDelegates = this.myDelegatesState.asReadonly();
  readonly grantedToMe = this.grantedToMeState.asReadonly();
  readonly delegationSaving = this.delegationSavingState.asReadonly();
  readonly delegationError = this.delegationErrorState.asReadonly();
  readonly selected = this.selectedState.asReadonly();
  readonly details = this.detailsState.asReadonly();
  readonly search = this.searchState.asReadonly();
  readonly loading = this.loadingState.asReadonly();
  readonly error = this.errorState.asReadonly();
  readonly birthdayScope = this.birthdayScopeState.asReadonly();
  readonly toastMessage = this.toastMessageState.asReadonly();

  readonly range = computed<DateRange>(() => rangeFor(this.viewState(), this.anchorState()));

  /**
   * Что реально запрашиваем у бэкенда. Всегда не меньше месяца вокруг опорной даты:
   * мини-календарь в левой панели рисует точки занятых дней, и в виде «День»
   * ему иначе нечего показывать.
   */
  private readonly fetchRange = computed<DateRange>(() => {
    const view = this.range();
    const month = rangeFor('month', this.anchorState());

    return {
      from: view.from < month.from ? view.from : month.from,
      to: view.to > month.to ? view.to : month.to,
    };
  });

  readonly title = computed(() => {
    const anchor = this.anchorState();
    switch (this.viewState()) {
      case 'day':
        return formatFullDate(anchor);
      case 'week':
        return formatWeekRange(startOfWeek(anchor));
      case 'month':
        return formatMonthYear(anchor);
      default:
        return String(anchor.getFullYear());
    }
  });

  /**
   * Что реально рисуется: галочки видимости, скоуп дней рождения и поиск фильтруют уже загруженное.
   * Клиентом, а не запросом — чтобы галочка отзывалась мгновенно и сетка не мигала.
   */
  readonly visibleOccurrences = computed(() => {
    const hidden = new Set(this.calendarsState().filter((x) => !x.isVisible).map((x) => x.id));
    const needle = this.searchState().trim().toLowerCase();
    const bdayCalendar = this.calendarsState().find((x) => x.name.toLowerCase().includes('рождения'));
    const bdayScope = this.birthdayScopeState();

    return this.occurrencesState().filter((occurrence) => {
      if (hidden.has(occurrence.calendarId)) {
        return false;
      }

      if (bdayCalendar && occurrence.calendarId === bdayCalendar.id) {
        const slug = occurrence.talkRoomSlug ?? '';
        const itemScope: BirthdayScope = slug === 'bday:fav' ? 'fav' : slug === 'bday:all' ? 'all' : 'dept';
        if (bdayScope === 'fav' && itemScope !== 'fav') {
          return false;
        }
        if (bdayScope === 'dept' && itemScope === 'all') {
          return false;
        }
      }

      return needle.length === 0 || occurrence.title.toLowerCase().includes(needle);
    });
  });

  load(): void {
    this.api
      .calendars()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (calendars) => this.calendarsState.set(calendars),
        error: () => this.errorState.set('Не удалось загрузить календари'),
      });

    this.api
      .participantLists()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (lists) => this.participantListsState.set(lists),
        error: () => this.errorState.set('Не удалось загрузить списки участников'),
      });

    this.api
      .rooms()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (rooms) => this.roomsState.set(rooms),
        error: () => this.errorState.set('Не удалось загрузить список переговорок'),
      });

    this.loadDelegations();
    this.refresh();
  }

  loadDelegations(): void {
    this.api
      .myDelegates()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (people) => this.myDelegatesState.set(people) });

    this.api
      .grantedToMe()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (people) => this.grantedToMeState.set(people) });
  }

  grantDelegation(delegateUserId: string): void {
    this.delegationSavingState.set(true);
    this.delegationErrorState.set(null);

    this.api
      .grantDelegation(delegateUserId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.delegationSavingState.set(false);
          this.loadDelegations();
        },
        error: () => {
          this.delegationSavingState.set(false);
          this.delegationErrorState.set('Не удалось выдать право — попробуйте ещё раз');
        },
      });
  }

  revokeDelegation(delegateUserId: string): void {
    this.delegationSavingState.set(true);
    this.delegationErrorState.set(null);

    this.api
      .revokeDelegation(delegateUserId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.delegationSavingState.set(false);
          this.loadDelegations();
        },
        error: () => {
          this.delegationSavingState.set(false);
          this.delegationErrorState.set('Не удалось забрать право — попробуйте ещё раз');
        },
      });
  }

  refresh(): void {
    const { from, to } = this.fetchRange();
    this.loadingState.set(true);

    this.api
      .events(toIso(from), toIso(to))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (occurrences) => {
          this.occurrencesState.set(occurrences);
          this.loadingState.set(false);
          this.errorState.set(null);
          this.refreshSelection();
        },
        error: () => {
          this.loadingState.set(false);
          this.errorState.set('Не удалось загрузить встречи');
        },
      });
  }

  setView(view: CalendarView): void {
    if (view === this.viewState()) {
      return;
    }

    this.viewState.set(view);
    this.refresh();
  }

  setSearch(text: string): void {
    this.searchState.set(text);
  }

  showToast(message: string): void {
    this.toastMessageState.set(message);
    if (this.toastTimer) {
      clearTimeout(this.toastTimer);
    }
    this.toastTimer = setTimeout(() => {
      if (this.toastMessageState() === message) {
        this.toastMessageState.set(null);
      }
    }, 2500);
  }

  setBirthdayScope(scope: BirthdayScope): void {
    this.birthdayScopeState.set(scope);
    const bdayCalendar = this.calendarsState().find((x) => x.name.toLowerCase().includes('рождения'));
    if (bdayCalendar && !bdayCalendar.isVisible) {
      this.toggleCalendar(bdayCalendar);
    }
    const label = scope === 'all' ? 'всех сотрудников' : scope === 'dept' ? 'моё подразделение' : 'только избранных';
    this.showToast(`Дни рождения: ${label}`);
  }

  today(): void {
    this.focus(new Date());
  }

  /** Выбор дня: в мини-календаре и месячной сетке он же переводит опорную дату. */
  focus(date: Date, view?: CalendarView): void {
    this.anchorState.set(startOfDay(date));
    if (view && view !== this.viewState()) {
      this.viewState.set(view);
    }

    this.refresh();
  }

  step(direction: -1 | 1): void {
    const anchor = this.anchorState();

    switch (this.viewState()) {
      case 'day':
        this.anchorState.set(addDays(anchor, direction));
        break;
      case 'week':
        this.anchorState.set(addDays(anchor, direction * 7));
        break;
      case 'month':
        this.anchorState.set(addMonths(anchor, direction));
        break;
      default:
        this.anchorState.set(addMonths(anchor, direction * 12));
        break;
    }

    this.refresh();
  }

  toggleCalendar(calendar: Calendar): void {
    const isVisible = !calendar.isVisible;
    this.patchCalendarLocally(calendar.id, isVisible);

    this.api
      .updateCalendar(calendar.id, { isVisible })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          // Не сохранилось — возвращаем галочку, чтобы интерфейс не врал.
          this.patchCalendarLocally(calendar.id, calendar.isVisible);
          this.errorState.set('Не удалось сохранить видимость календаря');
        },
      });
  }

  select(occurrence: Occurrence | null): void {
    if (!occurrence) {
      this.selectedState.set(null);
      this.detailsState.set(null);
      return;
    }

    const key: OccurrenceKey = {
      eventId: occurrence.eventId,
      occurrenceStartUtc: occurrence.occurrenceStartUtc,
    };

    this.selectedState.set(key);
    this.loadDetails(key);
  }

  createEvent(request: CreateEventRequest): void {
    this.api
      .createEvent(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (details) => {
          this.detailsState.set(details);
          this.selectedState.set({
            eventId: details.occurrence.eventId,
            occurrenceStartUtc: details.occurrence.occurrenceStartUtc,
          });
          this.refresh();
        },
        error: (err: HttpErrorResponse) => this.errorState.set(serverDetail(err) ?? 'Не удалось создать встречу'),
      });
  }

  updateEvent(key: OccurrenceKey, patch: UpdateEventRequest, scope: EditScope): void {
    this.api
      .updateEvent(key.eventId, patch, scope, scope === 'occurrence' ? key.occurrenceStartUtc : undefined)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (details) => {
          this.detailsState.set(details);
          this.refresh();
        },
        error: (err: HttpErrorResponse) => {
          this.errorState.set(serverDetail(err) ?? 'Не удалось сохранить изменения встречи');
          this.refresh();
        },
      });
  }

  /**
   * Перенос и растягивание в сетке. Время меняем в списке сразу, не дожидаясь ответа:
   * карточка не должна прыгать обратно под курсором.
   */
  moveOccurrence(occurrence: Occurrence, startUtc: Date, endUtc: Date): void {
    const patch: UpdateEventRequest = { startUtc: toIso(startUtc), endUtc: toIso(endUtc) };
    this.occurrencesState.update((all) =>
      all.map((item) =>
        isSameOccurrence(item, occurrence)
          ? { ...item, startUtc: patch.startUtc!, endUtc: patch.endUtc!, isMoved: item.recurrenceRule !== null }
          : item,
      ),
    );

    // Разовую встречу двигаем целиком, у серии — только это вхождение.
    const scope: EditScope = occurrence.recurrenceRule ? 'occurrence' : 'series';
    this.updateEvent(
      { eventId: occurrence.eventId, occurrenceStartUtc: occurrence.occurrenceStartUtc },
      patch,
      scope,
    );
  }

  deleteEvent(key: OccurrenceKey, scope: EditScope): void {
    this.api
      .deleteEvent(key.eventId, scope, scope === 'occurrence' ? key.occurrenceStartUtc : undefined)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.select(null);
          this.refresh();
        },
        error: () => this.errorState.set('Не удалось удалить встречу'),
      });
  }

  rsvp(eventId: string, status: ParticipantStatus): void {
    this.api
      .rsvp(eventId, status)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (details) => {
          this.detailsState.set(details);
          this.refresh();
        },
        error: () => this.errorState.set('Не удалось отправить ответ'),
      });
  }

  setReminder(key: OccurrenceKey, minutesBefore: number): void {
    this.updateEvent(key, { reminderMinutesBefore: minutesBefore }, 'series');
  }

  dismissError(): void {
    this.errorState.set(null);
  }

  private loadDetails(key: OccurrenceKey): void {
    this.api
      .eventDetails(key.eventId, key.occurrenceStartUtc)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (details) => this.detailsState.set(details),
        error: () => {
          this.detailsState.set(null);
          this.errorState.set('Не удалось открыть встречу');
        },
      });
  }

  /** Выбранная встреча могла уехать из видимого периода или исчезнуть — тогда закрываем панель. */
  private refreshSelection(): void {
    const key = this.selectedState();
    if (!key) {
      return;
    }

    const stillHere = this.occurrencesState().some(
      (x) => x.eventId === key.eventId && x.occurrenceStartUtc === key.occurrenceStartUtc,
    );

    if (!stillHere) {
      this.selectedState.set(null);
      this.detailsState.set(null);
    }
  }

  private patchCalendarLocally(id: string, isVisible: boolean): void {
    this.calendarsState.update((all) => all.map((x) => (x.id === id ? { ...x, isVisible } : x)));
  }
}

/**
 * Бэкенд отдаёт человекочитаемую причину отказа как ProblemDetails.detail (например,
 * «Переговорка занята») — показываем её вместо обезличенного «не удалось сохранить».
 */
function serverDetail(err: HttpErrorResponse): string | null {
  const detail = err.error?.detail;
  return typeof detail === 'string' && detail.trim().length > 0 ? detail : null;
}

export function isSameOccurrence(left: Occurrence, right: Occurrence | OccurrenceKey | null): boolean {
  return (
    right !== null &&
    left.eventId === right.eventId &&
    left.occurrenceStartUtc === right.occurrenceStartUtc
  );
}

export function rangeFor(view: CalendarView, anchor: Date): DateRange {
  switch (view) {
    case 'day': {
      const from = startOfDay(anchor);
      return { from, to: addDays(from, 1) };
    }
    case 'week': {
      const from = startOfWeek(anchor);
      return { from, to: addDays(from, 7) };
    }
    case 'month': {
      const from = startOfWeek(startOfMonth(anchor));
      return { from, to: addDays(from, MONTH_GRID_WEEKS * 7) };
    }
    default: {
      const from = startOfYear(anchor);
      return { from, to: new Date(anchor.getFullYear() + 1, 0, 1) };
    }
  }
}
