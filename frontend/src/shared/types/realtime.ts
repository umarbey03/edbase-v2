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

/* ==========================================================================
   BILDIRISHNOMA HUB'I — `/hubs/notifications` (R35/R36).

   🔴 KLIENT -> SERVER METODI YO'Q, VA BU ATAYLAB.

   Yuqoridagi ikki hub'da obuna metodi bor (`JoinSession`, `JoinThread`),
   chunki ularning qamrovi DARS yoki OQIM — klient qaysi xonaga kirishini
   aytishi kerak. Bu yerda qamrov ODAM: server ulanish egasini TOKENDAN
   aniqlaydi (`Clients.User`), ya'ni klient hech nima so'ramaydi va hech
   qanday xonaga qo'shilmaydi.

   Amaliy oqibat: `onreconnected` da QAYTA OBUNA BO'LISH SHART EMAS —
   guruh a'zoligi yo'q, ya'ni yo'qoladigan narsa ham yo'q. Guruh chati
   hub'idan asosiy farq shu.

   ★ "O'qildi" ham bu yerda YO'Q: u HOLATNI o'zgartiradi va REST'da
   (`POST /api/v1/notifications/read`). Ikki yo'l bo'lsa idempotentlik
   qoidasi ikki joyda yozilardi.
   ========================================================================== */

/**
 * Server -> Klient. Bildirishnoma kanalida tinglanadigan YAGONA hodisa.
 *
 * ★ TANASI `NotificationDto` — REST ro'yxatidagi element bilan AYNAN bir
 * xil tur (`GroupChatMessage` dagi AYNI qaror). Alohida "event" shakli
 * yasalsa, frontend bitta narsani ikki xil tahlil qilishga majbur bo'lardi
 * va ular vaqt o'tib ajralib ketardi.
 */
export const NotificationHubEvent = {
  NotificationCreated: 'NotificationCreated',
} as const
export type NotificationHubEventName =
  (typeof NotificationHubEvent)[keyof typeof NotificationHubEvent]
