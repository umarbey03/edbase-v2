import type { UserRoleName } from './api'

/**
 * SPEC 6-bo'lim — SignalR shartnomasi.
 * Metod va hodisa nomlari QAT'IY: satrlar faqat shu yerda e'lon qilinadi,
 * kod ichida "qo'lda" yozilmaydi (typo'ning oldini oladi).
 */

/** Klient -> Server (Hub metodlari) */
export const HubMethod = {
  JoinSession: 'JoinSession',
  LeaveSession: 'LeaveSession',
  SendMessage: 'SendMessage',
  RaiseHand: 'RaiseHand',
} as const
export type HubMethodName = (typeof HubMethod)[keyof typeof HubMethod]

/** Server -> Klient (klient shu nomlarni tinglaydi) */
export const HubEvent = {
  ChatMessage: 'ChatMessage',
  PresenceChanged: 'PresenceChanged',
  HandRaised: 'HandRaised',
  SessionEnded: 'SessionEnded',
} as const
export type HubEventName = (typeof HubEvent)[keyof typeof HubEvent]

/**
 * SPEC 4: `PresenceEntry(long UserId, string DisplayName, string Role, bool HandRaised, DateTimeOffset JoinedAt)`.
 * To'liq ro'yxat FAQAT `JoinSession` javobida bir marta keladi (SPEC 6.1).
 */
export interface PresenceEntry {
  userId: number
  displayName: string
  role: UserRoleName
  handRaised: boolean
  joinedAt: string
}

/** `PresenceChanged` — faqat DELTA (kim + umumiy son), to'liq ro'yxat emas. */
export interface PresenceChangedPayload {
  userId: number
  displayName: string
  role: UserRoleName
  joined: boolean
  count: number
}

export interface HandRaisedPayload {
  userId: number
  displayName: string
  raised: boolean
}

export interface SessionEndedPayload {
  sessionId: number
}

/** Hub ulanishining foydalanuvchiga ko'rsatiladigan holati. */
export type HubStatus = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected'
