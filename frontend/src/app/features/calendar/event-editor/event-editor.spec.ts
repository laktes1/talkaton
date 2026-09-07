import { Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Calendar } from '../../../core/api/models';
import { EventEditor, EventEditorSeed } from './event-editor';

interface TestableEditor {
  readonly selectedColor: Signal<string>;
  readonly tolkVideo: Signal<boolean>;
  readonly recordAndAi: Signal<boolean>;
  readonly repeatWeekly: Signal<boolean>;
}

describe('EventEditor', () => {
  const calendars: Calendar[] = [
    { id: 'cal-work', name: 'Рабочие встречи', color: '#4c8dff', isVisible: true, sortOrder: 0 },
    { id: 'cal-personal', name: 'Личное', color: '#2fbf71', isVisible: true, sortOrder: 1 },
  ];

  const seed = (patch: Partial<EventEditorSeed>): EventEditorSeed => ({
    mode: 'edit',
    eventId: 'e1',
    calendarId: 'cal-work',
    title: 'Планёрка',
    description: '',
    start: new Date('2026-09-09T09:00:00Z'),
    end: new Date('2026-09-09T10:00:00Z'),
    isAllDay: false,
    recurrenceRule: null,
    talkRoomSlug: '',
    participantIds: [],
    reminderMinutesBefore: 10,
    hasArtifacts: false,
    roomId: null,
    ...patch,
  });

  const open = (value: EventEditorSeed): TestableEditor => {
    const fixture = TestBed.createComponent(EventEditor);
    fixture.componentRef.setInput('seed', value);
    fixture.componentRef.setInput('calendars', calendars);
    fixture.componentRef.setInput('people', []);
    fixture.componentRef.setInput('participantLists', []);
    fixture.componentRef.setInput('rooms', []);
    fixture.detectChanges();
    return fixture.componentInstance as unknown as TestableEditor;
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [EventEditor] }).compileComponents();
  });

  it('в режиме создания включает видеовстречу и запись по умолчанию', () => {
    const editor = open(seed({ mode: 'create', eventId: null }));

    expect(editor.tolkVideo()).toBe(true);
    expect(editor.recordAndAi()).toBe(true);
    expect(editor.repeatWeekly()).toBe(false);
  });

  it('при правке читает тумблеры из самой встречи, а не из дефолтов формы', () => {
    const editor = open(seed({
      talkRoomSlug: '',
      hasArtifacts: false,
      recurrenceRule: 'FREQ=WEEKLY;BYDAY=WE',
    }));

    expect(editor.tolkVideo()).toBe(false);
    expect(editor.recordAndAi()).toBe(false);
    expect(editor.repeatWeekly()).toBe(true);
  });

  it('при правке включает тумблеры, когда у встречи есть комната и артефакты', () => {
    const editor = open(seed({ talkRoomSlug: 'room-abc', hasArtifacts: true }));

    expect(editor.tolkVideo()).toBe(true);
    expect(editor.recordAndAi()).toBe(true);
  });

  it('подсвечивает цветовую точку календаря встречи', () => {
    expect(open(seed({ calendarId: 'cal-personal' })).selectedColor()).toBe('teal');
    expect(open(seed({ calendarId: 'cal-work' })).selectedColor()).toBe('blue');
  });
});
