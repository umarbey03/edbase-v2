import { lookup } from '@/shared/lib/lookup'
import type { GroupChatChannelName, GroupChatThreadDto } from '@/shared/types'

/**
 * GURUH CHATI — har guruhning DOIMIY suhbati (dars vaqtidan tashqarida ham).
 *
 * Eski ilovada bu kundalik ishlatiladigan funksiya edi: o'quvchilar savol
 * beradi, ustoz/kurator javob yozadi. Jonli dars chatidan (`entities/message`)
 * PRINSIPIAL farqi — u dars tugashi bilan yopiladi, bu esa doimiy.
 */

/**
 * Xabar uzunligi chegarasi — SERVER shartnomasining nusxasi.
 *
 * Server 2000 belgida kesadi (surrogat juftlikni buzmasdan), lekin klientda
 * ham cheklaymiz: jimgina kesilgan xabar foydalanuvchi uchun ma'lumot
 * yo'qolishi bo'lardi. `entities/direct-message` dagi `DM_BODY_MAX` bilan
 * bir xil qiymat — lekin ATAYLAB o'sha yerdan import qilinmaydi: bular ikki
 * xil endpoint va biri o'zgarganda ikkinchisi jimgina ergashib ketmasligi
 * kerak.
 */
export const GROUP_CHAT_BODY_MAX = 2000

/**
 * Serverning tezlik chegarasi: 10 sekundda 10 xabar
 * `(guruh, kanal, foydalanuvchi)` bo'yicha. ★ REST va hub BITTA budjetni
 * bo'lishadi, ya'ni klient tomonda ularni alohida hisoblash mumkin emas —
 * shu sababli bu yerda "oldindan bloklash" YO'Q, faqat serverning 429 javobi
 * (`Retry-After`) hurmat qilinadi.
 */
export const GROUP_CHAT_RATE_WINDOW_SECONDS = 10

/**
 * Server tezlik chegarasi xabarining BARQAROR bo'lagi.
 *
 * NEGA MATN BO'YICHA ANIQLANADI: REST yo'lida chegara `429` + `Retry-After`
 * bilan keladi va uni aniq o'qish mumkin, HUB yo'lida esa faqat
 * `HubException` matni bor — status kodi ham, `Retry-After` ham YO'Q
 * (jonli tekshirildi). Ya'ni hub orqali yuborilganda buni bilishning
 * boshqa yo'li yo'q.
 *
 * Matn serverdan AYNAN shunday keladi: "Juda tez yozyapsiz. Bir necha
 * soniyadan keyin urinib ko'ring." Faqat birinchi, o'zgarmaydigan bo'lagi
 * solishtiriladi — jumla oxiri qayta yozilsa ham tekshiruv ishlayveradi.
 */
export const GROUP_CHAT_RATE_LIMIT_MARKER = 'Juda tez yozyapsiz'

/**
 * Tarixning bitta sahifasi. Eski ilova butun tarixni bir zarbada yuklardi;
 * 50 ta — telefon uchun ham yengil, "yuqoriga scroll" esa qolganini oladi.
 */
export const GROUP_CHAT_PAGE_SIZE = 50

/**
 * Kanal nomlari — eski ilovadagi matnlarning AYNAN nusxasi:
 *  • `student.html` (renderChatList): "Ustoz chati" / "Kurator chati";
 *  • `teacher.html` (initTeacherChat): `isT ? 'Ustoz chati' : 'Kurator chati'`.
 * O'zgartirilmaydi — foydalanuvchi ro'yxatdan aynan shu so'zlarni qidiradi.
 */
const CHANNEL_LABELS: Record<GroupChatChannelName, string> = {
  Teacher: 'Ustoz chati',
  Curator: 'Kurator chati',
}

export function channelLabel(channel: string): string {
  return lookup(CHANNEL_LABELS, channel, channel)
}

/**
 * Kanal rangi. Eski ilovada qat'iy edi va IKKALA panelda bir xil:
 * ustoz oqimi OLTIN (`--accent`, `.tchat-badge.teach`), kurator oqimi
 * FIROʻZA (`#22d3ee`, `.tchat-badge.assist`). `BaseBadge` ning `teacher`
 * ohangi oltin, `assistant` ohangi moviy — aynan shunga to'g'ri keladi,
 * shuning uchun yangi rang e'lon qilinmadi.
 */
const CHANNEL_TONES: Record<GroupChatChannelName, 'teacher' | 'assistant'> = {
  Teacher: 'teacher',
  Curator: 'assistant',
}

export function channelTone(channel: string): 'teacher' | 'assistant' {
  return lookup(CHANNEL_TONES, channel, 'teacher')
}

/**
 * Suhbat sarlavhasi: `ATF-1 - Ustoz chati`.
 *
 * Eski `student.html` dagi `openChatRoom(g.id, 'teacher', g.name + ' - Ustoz
 * chati')` chaqiruvidan AYNAN — ajratgich ham o'sha (probel-tire-probel).
 */
export function threadTitle(groupName: string, channel: string): string {
  return `${groupName} - ${channelLabel(channel)}`
}

/**
 * Ro'yxatdagi ikkinchi qator: oxirgi xabar ko'rinishi.
 *
 * `lastMessageSenderName` old qo'shiladi (Telegram va eski ilovadagidek) —
 * guruh chatida "kim yozgani" xabarning o'zi kabi muhim: ustoz javob berdimi
 * yoki yana bir o'quvchi savol berdimi, bir qarashda ko'rinadi.
 */
export function threadSubtitle(thread: GroupChatThreadDto): string {
  const preview = thread.lastMessagePreview ?? ''
  if (preview.length === 0) return 'Hali xabar yo‘q'
  const sender = thread.lastMessageSenderName ?? ''
  return sender.length > 0 ? `${sender}: ${preview}` : preview
}

/**
 * Ro'yxat qatorining BARQAROR kaliti.
 *
 * ★ `groupId` YOLG'IZ YETMAYDI: `/threads` (guruh, kanal) juftligi uchun
 * qator qaytaradi va o'quvchida bitta guruh IKKI marta uchraydi
 * (jonli tekshirildi). Kalit sifatida faqat `groupId` ishlatilsa, Vue ikkita
 * qatorni bitta deb hisoblab, ikkinchisini umuman chizmasdi.
 */
export function threadKey(groupId: number, channel: string): string {
  return `${groupId}:${channel}`
}
