import { addMember } from '@/entities/group'
import { fetchGroupConversion } from '@/entities/subscription'
import { formatMoney } from '@/shared/lib/money'
import { askConfirm } from '@/shared/lib/useConfirm'
import type { GroupMemberDto } from '@/shared/types'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  OBUNANI GURUHGA O'TKAZISH (2026-10-29, loyiha egasi qarori Q-2)
 * ════════════════════════════════════════════════════════════════════════
 *
 * 🔴 ILGARI: mustaqil o'qish obunasi bor o'quvchini guruhga qo'shib
 *   bo'lmasdi (409) va xodim obunani TO'LIQ bekor qilardi — qolgan kunlar
 *   puli jimgina yo'qolardi (audit P0-3).
 * ★ ENDI qo'shishdan OLDIN server so'raladi: obunada necha kun va qancha
 *   pul qolgan. Bo'lsa — xodim "balansga o'tkazib qo'shilsinmi?" ni
 *   tasdiqlaydi va so'rov `convertSubscription: true` bilan ketadi. Server
 *   obunani yopish, pulni balansga yozish va a'zolikni BITTA amalda bajaradi.
 *
 * @returns `false` — obuna yo'q, oddiy qo'shish; `true` — xodim o'tkazishni
 *   tasdiqladi; `null` — xodim voz kechdi (hech narsa yuborilmaydi).
 */
export async function askSubscriptionConversion(studentId: number): Promise<boolean | null> {
  const conversion = await fetchGroupConversion(studentId)
  if (!conversion.hasLiveSubscription) return false

  const ok = await askConfirm({
    title: 'Mustaqil o‘qish obunasi bor',
    message:
      `Obunada ${conversion.daysLeft} kun (${formatMoney(conversion.amount)}) qoldi — `
      + 'balansga o‘tkazib guruhga qo‘shilsinmi?',
    confirmLabel: 'O‘tkazish va qo‘shish',
    cancelLabel: 'Ortga',
    tone: 'warning',
    details: [
      'Joriy obuna shu zahoti yopiladi, oldindan to‘langan davrlar bekor qilinadi.',
      'Ishlatilmagan pul o‘quvchi balansiga tushadi va guruh to‘lovlariga ishlatiladi.',
      'Hammasi moliya jurnaliga yoziladi.',
    ],
  })

  return ok ? true : null
}

/**
 * Mavjud o'quvchini guruhga qo'shish — obuna bo'lsa avval o'tkazishni so'raydi.
 * Guruhga qo'shiladigan HAR joy (yangi a'zo, arxivdan qaytarish, boshqa guruhga
 * qaytarish) shu yo'ldan o'tadi — qoida bitta joyda.
 *
 * @returns `null` — xodim obunani o'tkazishdan voz kechdi, hech narsa qilinmadi.
 */
export async function addMemberWithConversion(
  groupId: number,
  studentId: number,
): Promise<GroupMemberDto | null> {
  const convert = await askSubscriptionConversion(studentId)
  if (convert === null) return null
  return addMember(groupId, { studentId, convertSubscription: convert })
}
