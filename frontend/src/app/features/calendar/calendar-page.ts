import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  HostListener,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CalendarStore, CalendarView } from './calendar-store';
import { LeftPanel } from './left-panel/left-panel';
import { CalendarToolbar } from './calendar-toolbar/calendar-toolbar';
import { WeekGrid, OccurrenceMove } from './week-grid/week-grid';
import { MonthGrid } from './month-grid/month-grid';
import { YearView } from './year-view/year-view';
import { EventDetailsPanel } from './event-details/event-details';
import { EventDraft, EventEditor, EventEditorSeed } from './event-editor/event-editor';
import {
  ParticipantListDraft,
  ParticipantListEditor,
} from './participant-list-editor/participant-list-editor';
import { DelegationManager } from './delegation-manager/delegation-manager';
import { TalkatonApi } from '../../core/api/talkaton-api';
import { ReminderService } from '../reminders/reminder.service';
import { Calendar, EditScope, Health, Occurrence, ParticipantList, ParticipantStatus, User } from '../../core/api/models';
import {
  addDays,
  addMinutes,
  dayKey,
  minutesBetween,
  startOfDay,
} from '../../core/time/date-utils';

/** Встреча по умолчанию — час, начиная с ближайшего целого часа выбранного дня. */
const DEFAULT_DURATION_MINUTES = 60;
const DEFAULT_START_HOUR = 10;

@Component({
  selector: 'app-calendar-page',
  imports: [
    LeftPanel,
    CalendarToolbar,
    WeekGrid,
    MonthGrid,
    YearView,
    EventDetailsPanel,
    EventEditor,
    ParticipantListEditor,
    DelegationManager,
  ],
  providers: [CalendarStore],
  templateUrl: './calendar-page.html',
  styleUrl: './calendar-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CalendarPage {
  private readonly api = inject(TalkatonApi);
  private readonly reminders = inject(ReminderService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly store = inject(CalendarStore);

  protected readonly people = signal<User[]>([]);
  protected readonly health = signal<Health | null>(null);
  protected readonly editorSeed = signal<EventEditorSeed | null>(null);
  protected readonly listEditorOpen = signal(false);
  protected readonly listSaving = signal(false);
  protected readonly listError = signal<string | null>(null);
  protected readonly delegationManagerOpen = signal(false);

  /** Дни для сетки: один для вида «День», семь для недели, сорок два для месяца. */
  protected readonly days = computed(() => {
    const { from, to } = this.store.range();
    const count = Math.round((to.getTime() - from.getTime()) / 86_400_000);
    return Array.from({ length: count }, (_, index) => addDays(from, index));
  });

  protected readonly busyDays = computed(() => {
    const keys = new Set<string>();
    for (const occurrence of this.store.visibleOccurrences()) {
      const end = new Date(occurrence.endUtc);
      for (let day = startOfDay(new Date(occurrence.startUtc)); day < end; day = addDays(day, 1)) {
        keys.add(dayKey(day));
      }
    }

    return keys;
  });

  constructor() {
    this.store.load();

    this.api
      .users()
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: (people) => this.people.set(people),
        error: () => this.people.set([]),
      });

    this.api
      .health()
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: (health) => this.health.set(health),
        error: () => this.health.set(null),
      });
  }

  protected onViewChanged(view: CalendarView): void {
    this.store.setView(view);
  }

  protected onDayPicked(date: Date): void {
    this.store.focus(date);
  }

  /** Из месяца и года щелчок по дню проваливает в этот день — так это работает везде. */
  protected onDayDrilledDown(date: Date): void {
    this.store.focus(date, 'day');
  }

  protected onCalendarToggled(calendar: Calendar): void {
    this.store.toggleCalendar(calendar);
  }

  protected openListEditor(): void {
    this.listError.set(null);
    this.listEditorOpen.set(true);
  }

  protected closeListEditor(): void {
    if (!this.listSaving()) {
      this.listEditorOpen.set(false);
      this.listError.set(null);
    }
  }

  protected onListCreated(draft: ParticipantListDraft): void {
    if (this.listSaving()) {
      return;
    }

    this.listSaving.set(true);
    this.listError.set(null);
    this.api
      .createParticipantList(draft.name, draft.color, draft.memberIds)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.listSaving.set(false);
          this.listEditorOpen.set(false);
          this.store.load();
          this.store.showToast(`Список «${draft.name}» создан`);
        },
        error: () => {
          this.listSaving.set(false);
          this.listError.set('Не удалось создать список. Проверьте соединение и попробуйте ещё раз.');
        },
      });
  }

  protected openDelegationManager(): void {
    this.delegationManagerOpen.set(true);
  }

  protected closeDelegationManager(): void {
    if (!this.store.delegationSaving()) {
      this.delegationManagerOpen.set(false);
    }
  }

  protected onDelegationGranted(delegateUserId: string): void {
    this.store.grantDelegation(delegateUserId);
  }

  protected onDelegationRevoked(delegateUserId: string): void {
    this.store.revokeDelegation(delegateUserId);
  }

  protected onListDeleted(list: ParticipantList): void {
    this.api
      .deleteParticipantList(list.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.store.load();
          this.store.showToast(`Список «${list.name}» удалён`);
        },
        error: () => {
          this.store.showToast(`Не удалось удалить список «${list.name}»`);
        },
      });
  }

  protected onListClicked(list: ParticipantList): void {
    this.store.showToast(`Список «${list.name}»: ${list.members.length} участников`);
  }

  protected onOccurrenceSelected(occurrence: Occurrence): void {
    this.store.select(occurrence);
  }

  protected onOccurrenceMoved(move: OccurrenceMove): void {
    this.store.moveOccurrence(move.occurrence, move.start, move.end);
    this.reminders.reload();
    this.store.showToast('Встреча перенесена');
  }

  protected onRsvp(status: ParticipantStatus): void {
    const key = this.store.selected();
    if (key) {
      this.store.rsvp(key.eventId, status);
    }
  }

  protected onReminderChanged(minutes: number): void {
    const key = this.store.selected();
    if (key) {
      this.store.setReminder(key, minutes);
      this.reminders.reload();
    }
  }

  protected onDeleteRequested(scope: EditScope): void {
    const key = this.store.selected();
    if (key) {
      this.store.deleteEvent(key, scope);
      this.reminders.reload();
    }
  }

  protected openCreateEditor(slot?: Date): void {
    const calendars = this.store.calendars();
    if (calendars.length === 0) {
      return;
    }

    const start = slot ?? this.defaultSlot();
    this.editorSeed.set({
      mode: 'create',
      eventId: null,
      calendarId: calendars[0].id,
      title: '',
      description: '',
      start,
      end: addMinutes(start, DEFAULT_DURATION_MINUTES),
      isAllDay: false,
      recurrenceRule: null,
      talkRoomSlug: '',
      participantIds: [],
      reminderMinutesBefore: 10,
      hasArtifacts: false,
      roomId: null,
    });
  }

  protected openEditEditor(): void {
    const details = this.store.details();
    if (!details) {
      return;
    }

    const occurrence = details.occurrence;
    this.editorSeed.set({
      mode: 'edit',
      eventId: occurrence.eventId,
      calendarId: occurrence.calendarId,
      title: occurrence.title,
      description: details.description ?? '',
      start: new Date(occurrence.startUtc),
      end: new Date(occurrence.endUtc),
      isAllDay: occurrence.isAllDay,
      recurrenceRule: occurrence.recurrenceRule,
      talkRoomSlug: occurrence.talkRoomSlug ?? '',
      participantIds: details.participants.filter((x) => !x.isOrganizer).map((x) => x.userId),
      reminderMinutesBefore: occurrence.reminderMinutesBefore ?? 10,
      hasArtifacts: occurrence.artifactCount > 0,
      roomId: occurrence.roomId,
    });
  }

  protected onEditorSaved(draft: EventDraft): void {
    const seed = this.editorSeed();
    this.editorSeed.set(null);

    if (!seed) {
      return;
    }

    if (seed.mode === 'create') {
      this.store.createEvent({
        calendarId: draft.calendarId,
        title: draft.title,
        description: draft.description,
        startUtc: draft.startUtc,
        endUtc: draft.endUtc,
        isAllDay: draft.isAllDay,
        recurrenceRule: draft.recurrenceRule,
        talkRoomSlug: draft.talkRoomSlug,
        participantIds: draft.participantIds,
        reminderMinutesBefore: draft.reminderMinutesBefore,
        generateArtifacts: draft.generateArtifacts,
        roomId: draft.roomId,
        onBehalfOfUserId: draft.onBehalfOfUserId,
      });
    } else if (seed.eventId) {
      this.store.updateEvent(
        this.store.selected() ?? { eventId: seed.eventId, occurrenceStartUtc: '' },
        {
          calendarId: draft.calendarId,
          title: draft.title,
          description: draft.description,
          startUtc: draft.startUtc,
          endUtc: draft.endUtc,
          isAllDay: draft.isAllDay,
          recurrenceRule: draft.recurrenceRule,
          // Пустое правило значит «сделать разовой», а не «не трогать» — отдельным флагом.
          clearRecurrence: draft.recurrenceRule === null,
          talkRoomSlug: draft.talkRoomSlug,
          participantIds: draft.participantIds,
          reminderMinutesBefore: draft.reminderMinutesBefore,
          generateArtifacts: draft.generateArtifacts,
          roomId: draft.roomId ?? undefined,
          // Пустая переговорка значит «снять бронь», а не «не трогать» — тем же приёмом,
          // что и clearRecurrence выше.
          clearRoom: draft.roomId === null,
        },
        'series',
      );
    }

    this.reminders.reload();
  }

  /** Быстрые клавиши из прототипа работают, только когда фокус не находится в контроле. */
  @HostListener('document:keydown', ['$event'])
  protected onShortcut(event: KeyboardEvent): void {
    if (event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) {
      return;
    }

    const target = event.target instanceof HTMLElement ? event.target : null;
    if (target?.closest('input, textarea, select, button, [contenteditable="true"]')) {
      return;
    }

    if (event.key === 'Escape') {
      if (this.editorSeed()) {
        this.editorSeed.set(null);
      } else if (this.listEditorOpen()) {
        this.closeListEditor();
      } else if (this.delegationManagerOpen()) {
        this.closeDelegationManager();
      } else if (this.store.details()) {
        this.store.select(null);
      }
      return;
    }

    if (this.editorSeed() || this.listEditorOpen() || this.delegationManagerOpen()) {
      return;
    }

    const key = event.key.toLocaleLowerCase('ru');
    const views: Partial<Record<string, CalendarView>> = {
      d: 'day',
      w: 'week',
      m: 'month',
      y: 'year',
    };
    const view = views[key];
    if (view) {
      this.store.setView(view);
      return;
    }

    if (key === 't') {
      this.store.today();
    } else if (key === 'c') {
      this.openCreateEditor();
    } else if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
      event.preventDefault();
      this.store.step(event.key === 'ArrowLeft' ? -1 : 1);
    }
  }

  private defaultSlot(): Date {
    const anchor = this.store.anchor();
    const now = new Date();

    // Сегодня предлагаем ближайший целый час, в другой день — рабочее утро.
    if (startOfDay(now).getTime() === anchor.getTime()) {
      const nextHour = new Date(now);
      nextHour.setMinutes(0, 0, 0);
      return addMinutes(nextHour, minutesBetween(nextHour, now) > 0 ? 60 : 0);
    }

    return addMinutes(anchor, DEFAULT_START_HOUR * 60);
  }
}
