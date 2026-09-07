import { WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { User } from '../../../core/api/models';
import { ParticipantListDraft, ParticipantListEditor } from './participant-list-editor';

interface TestableEditor {
  readonly name: WritableSignal<string>;
  readonly query: WritableSignal<string>;
  toggle(userId: string): void;
  submit(): void;
}

describe('ParticipantListEditor', () => {
  const people: User[] = [
    {
      id: 'a3a2e420-10e7-42f8-84a8-2ec0fb9de2d7',
      displayName: 'Алина Мороз',
      timeZoneId: 'Asia/Yekaterinburg',
      avatarColorIndex: 1,
      bufferBeforeMinutes: 0,
      bufferAfterMinutes: 0,
    },
    {
      id: '17a3c980-86a9-4db4-862e-9c7048357464',
      displayName: 'Денис Волков',
      timeZoneId: 'Europe/Moscow',
      avatarColorIndex: 2,
      bufferBeforeMinutes: 0,
      bufferAfterMinutes: 0,
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ParticipantListEditor] }).compileComponents();
  });

  it('показывает людей и фильтрует их по имени', () => {
    const fixture = TestBed.createComponent(ParticipantListEditor);
    fixture.componentRef.setInput('people', people);
    fixture.detectChanges();

    const component = fixture.componentInstance as unknown as TestableEditor;
    component.query.set('денис');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Денис Волков');
    expect(fixture.nativeElement.textContent).not.toContain('Алина Мороз');
  });

  it('отдаёт название и выбранных участников', () => {
    const fixture = TestBed.createComponent(ParticipantListEditor);
    fixture.componentRef.setInput('people', people);
    const saved: ParticipantListDraft[] = [];
    fixture.componentInstance.saved.subscribe((draft) => saved.push(draft));
    fixture.detectChanges();

    const component = fixture.componentInstance as unknown as TestableEditor;
    component.name.set('Дизайн-ревью');
    component.toggle(people[0].id);
    fixture.detectChanges();
    component.submit();

    expect(saved).toEqual([{ name: 'Дизайн-ревью', color: 'blue', memberIds: [people[0].id] }]);
  });

  it('не создаёт пустой список', () => {
    const fixture = TestBed.createComponent(ParticipantListEditor);
    fixture.componentRef.setInput('people', people);
    const saved: ParticipantListDraft[] = [];
    fixture.componentInstance.saved.subscribe((draft) => saved.push(draft));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('form') as HTMLFormElement).requestSubmit();
    fixture.detectChanges();

    expect(saved).toEqual([]);
    expect(fixture.nativeElement.textContent).toContain('Укажите название списка');
  });
});
