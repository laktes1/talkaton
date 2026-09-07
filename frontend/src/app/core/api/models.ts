/** Контракты `/api`. Один в один с DTO бэкенда — руками ничего не домысливаем. */

export interface User {
  id: string;
  displayName: string;
  timeZoneId: string;
  avatarColorIndex: number;
  /** Резервное время до/после встречи (Этап 7.5), в минутах. */
  bufferBeforeMinutes: number;
  bufferAfterMinutes: number;
}

export interface Calendar {
  id: string;
  name: string;
  color: string;
  isVisible: boolean;
  sortOrder: number;
}

export type ParticipantStatus = 'accepted' | 'declined' | 'tentative';

export type ArtifactKind = 'recording' | 'protocol' | 'board' | 'tasks';

/** Одно вхождение встречи в сетке. У разовой встречи оно единственное. */
export interface Occurrence {
  eventId: string;
  /** Ключ вхождения внутри серии — с ним возвращаемся в PATCH и DELETE. */
  occurrenceStartUtc: string;
  startUtc: string;
  endUtc: string;
  title: string;
  isAllDay: boolean;
  calendarId: string;
  calendarName: string;
  calendarColor: string;
  recurrenceRule: string | null;
  isMoved: boolean;
  talkRoomSlug: string | null;
  organizerId: string;
  organizerName: string;
  isOrganizer: boolean;
  myStatus: ParticipantStatus | null;
  participantCount: number;
  artifactCount: number;
  reminderMinutesBefore: number | null;
  roomId: string | null;
  roomName: string | null;
  createdByUserId: string;
  createdByName: string;
}

export interface Participant {
  userId: string;
  displayName: string;
  status: ParticipantStatus;
  isOrganizer: boolean;
  avatarColorIndex: number;
}

export interface Artifact {
  id: string;
  kind: ArtifactKind;
  title: string;
  subtitle: string | null;
  url: string | null;
}

export interface EventDetails {
  occurrence: Occurrence;
  description: string | null;
  participants: Participant[];
  artifacts: Artifact[];
}

export interface ParticipantList {
  id: string;
  name: string;
  color: string;
  sortOrder: number;
  members: User[];
}

export interface CreateEventRequest {
  calendarId: string;
  title: string;
  description?: string | null;
  startUtc: string;
  endUtc: string;
  isAllDay: boolean;
  recurrenceRule?: string | null;
  talkRoomSlug?: string | null;
  participantIds?: string[];
  reminderMinutesBefore?: number | null;
  generateArtifacts?: boolean;
  roomId?: string | null;
  onBehalfOfUserId?: string | null;
}

export interface UpdateEventRequest {
  calendarId?: string;
  title?: string;
  description?: string | null;
  startUtc?: string;
  endUtc?: string;
  isAllDay?: boolean;
  recurrenceRule?: string | null;
  clearRecurrence?: boolean;
  talkRoomSlug?: string | null;
  participantIds?: string[];
  reminderMinutesBefore?: number;
  generateArtifacts?: boolean;
  roomId?: string | null;
  clearRoom?: boolean;
}

/** Одна сторона делегирования (Этап 7.6) — владелец или делегат, смотря какой список. */
export interface DelegationPerson {
  userId: string;
  displayName: string;
}

/** Переговорка (Этап 7.2) — общий ресурс для выбора при создании встречи. */
export interface Room {
  id: string;
  name: string;
  capacity: number;
}

/** Занятость одной переговорки за период — тот же формат, что и грид занятости людей. */
export interface RoomAvailability {
  roomId: string;
  busyBlocks: BusyBlock[];
}

/** К чему относится правка: ко всей серии или к одному вхождению. */
export type EditScope = 'series' | 'occurrence';

/** Один занятый интервал в гриде занятости — без темы встречи, только время. */
export interface BusyBlock {
  startUtc: string;
  endUtc: string;
}

/** Занятость одного участника за запрошенный период (Этап 7.1). */
export interface UserAvailability {
  userId: string;
  busyBlocks: BusyBlock[];
}

export interface Health {
  status: 'healthy' | 'degraded';
  database: 'up' | 'down';
  version: string;
  utcNow: string;
}
