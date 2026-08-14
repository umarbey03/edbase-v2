import type { NotificationDto, NotificationKindName } from '@/shared/types'
import type { IconName } from '@/shared/ui'

/**
 * ILOVA ICHIDAGI BILDIRISHNOMA — qo'ng'iroqcha ro'yxati (R35/R36).
 *
 * Loyiha egasi: *"vazifa tekshirilgan avtomatik studentda ham yangilanish
 * kerak, page refresh kerak bo'lmasin, va notification kelsin"*.
 *
 * ★ BU CHAT EMAS. `entities/message` (kurator DM) va `entities/group-chat`
 * ikkalasi ham SUHBAT: ikki tomon yozadi, tarix yuqoriga suriladi. Bu esa
 * BIR TOMONLAMA hodisa oqimi — javob yozib bo'lmaydi, faqat "o'qildi"
 * belgilanadi. Shuning uchun ular bilan bitta entity qilinmadi.
 */

/**
 * Bir sahifada nechta qator.
 *
 * ★ 20 — chat sahifasidagi 50 dan KICHIK va bu ataylab: ochiluvchi panel
 * ~8 qator balandlikda ko'rinadi, ya'ni 50 ta qator uch ekran skroll
 * bo'lardi. Server chegarasi 50 (`NotificationFeed.MaxTake`), ya'ni bu
 * qiymat undan oshib ketmaydi.
 */
export const NOTIFICATION_PAGE_SIZE = 20

/**
 * Nishonda ko'rsatiladigan eng katta raqam — undan oshgani `99+`.
 *
 * ★ NEGA CHEGARA KERAK: ustoz 50 ta ishni bir o'tirishda baholaydi
 * (talab §"burst"). Uch xonali raqam qo'ng'iroqcha yonidagi doirani
 * cho'zib, appbar joylashuvini buzardi — telefonda esa "keyingi dars"
 * chipini siqib qo'yardi.
 */
export const NOTIFICATION_BADGE_MAX = 99

/** Nishon matni (`99+` chegarasi bilan). */
export function badgeLabel(count: number): string {
  if (count <= 0) return ''
  return count > NOTIFICATION_BADGE_MAX ? `${NOTIFICATION_BADGE_MAX}+` : String(count)
}

/**
 * Hodisa turiga mos ikonka.
 *
 * ★ NEGA `Record` VA `switch` EMAS: `NotificationKindName` — birlashma
 * (union) tur, ya'ni yangi tur qo'shilganda TypeScript shu xaritada
 * yetishmayotgan kalitni ko'rsatadi. `switch` da esa `default` tarmog'i
 * uni jimgina yutib yuborardi.
 */
const KIND_ICON: Record<NotificationKindName, IconName> = {
  SubmissionGraded: 'check-square',
}

export function notificationIcon(kind: NotificationKindName): IconName {
  return KIND_ICON[kind] ?? 'bell'
}

/**
 * Bildirishnoma bosilganda qaysi marshrutga o'tiladi.
 *
 * ★ MARSHRUT NOMI BO'YICHA, URL bo'yicha EMAS: `router/index.ts` da yo'l
 * o'zgarsa nom saqlanadi va bu yer buzilmaydi.
 *
 * ⚠️ `entityId` — javob (`submission`) Id'si, LEKIN o'quvchi paneli
 * javobni ALOHIDA sahifada ko'rsatmaydi: u vazifalar ro'yxatidagi
 * kartochka ichida. Shuning uchun ro'yxatga o'tamiz va bu ATAYLAB:
 * mavjud bo'lmagan sahifaga havola qilish "bosdim — hech nima
 * bo'lmadi" tajribasini berardi.
 */
export function notificationRouteName(kind: NotificationKindName): string {
  switch (kind) {
    case 'SubmissionGraded':
      return 'student-assignments'
    default:
      return 'student-home'
  }
}

/**
 * Ro'yxatni `id` bo'yicha DEDUPE qilib birlashtiradi.
 *
 * NEGA KERAK: realtime hodisasi va REST sahifasi BIR XIL qatorni olib
 * kelishi mumkin (hub xabari kelgan payt ro'yxat so'rovi ham ketgan
 * bo'lsa). Guruh chatida aynan shu holat kuzatilgan va u yerda ham
 * dedupe `id` bo'yicha qilinadi.
 */
export function mergeNotifications(
  existing: readonly NotificationDto[],
  incoming: readonly NotificationDto[],
): NotificationDto[] {
  const byId = new Map<number, NotificationDto>()

  for (const item of existing) byId.set(item.id, item)
  // Yangisi ESKISINING ustiga yoziladi: server holati doim ustun
  // (masalan "o'qildi" bayrog'i boshqa qurilmada o'zgargan bo'lishi mumkin).
  for (const item of incoming) byId.set(item.id, item)

  return [...byId.values()].sort((a, b) => b.id - a.id)
}
