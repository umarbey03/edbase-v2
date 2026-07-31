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

/* ==========================================================================
   GURUH CHATI HUB'I — `/hubs/group-chat`.

   Jonli dars hub'idan ALOHIDA ro'yxat: metod nomlari ustma-ust tushib qolsa
   (`SendMessage` ikkalasida ham bor!) noto'g'ri hub'ga chaqirish xatosi
   kompilyatsiyada emas, ish vaqtida chiqardi.
   ========================================================================== */

/** Klient -> Server. */
export const GroupChatHubMethod = {
  /** `(groupId, channel?)` -> `GroupChatAccessDto` (★ OBYEKT, massiv emas). */
  JoinThread: 'JoinThread',
  /** `(groupId, channel)` -> void. */
  LeaveThread: 'LeaveThread',
  /** `(groupId, channel?, body)` -> `GroupChatMessageDto`. */
  SendMessage: 'SendMessage',
} as const
export type GroupChatHubMethodName =
  (typeof GroupChatHubMethod)[keyof typeof GroupChatHubMethod]

/**
 * Server -> Klient. ★ Guruh chatida tinglanadigan YAGONA hodisa.
 *
 * ★ YUBORUVCHI O'ZI HAM SHU HODISANI OLADI (jonli tekshirildi: o'quvchi
 * `SendMessage` chaqirgach, javobda ham, hodisada ham AYNAN bitta `id` keldi).
 * Shu sababli xabarlar `id` bo'yicha DEDUPE qilinadi — aks holda yuboruvchi
 * o'z xabarini ikki marta ko'rardi.
 */
export const GroupChatHubEvent = {
  GroupChatMessage: 'GroupChatMessage',
} as const
export type GroupChatHubEventName =
  (typeof GroupChatHubEvent)[keyof typeof GroupChatHubEvent]
