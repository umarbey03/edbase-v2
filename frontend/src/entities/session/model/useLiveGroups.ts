import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import type { ComputedRef } from 'vue'

import { fetchLiveSessions } from '../api/session-api'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  HOZIR JONLI DARSI BOR GURUHLAR (2026-08-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasi: *"dars boshlangan guruhlarda card rangi o'zgarsin"* va
 * chatda jonli ekanini ko'rsatuvchi animatsiya bo'lsin.
 *
 * ★ YANGI ENDPOINT YOZILMADI: mavjud `GET /live-sessions` allaqachon
 * foydalanuvchi ko'ra oladigan darslarni holati bilan qaytaradi
 * (`Admin`/`Academic` — hammasini, ustoz — o'zinikini, o'quvchi — o'z
 * guruhlarinikini). Guruh bo'yicha "jonlimi" degan bayroqni serverga
 * qo'shish AYNI ma'lumotni ikkinchi marta uzatish bo'lardi.
 *
 * ★ KESH BO'LINADI: `queryKey` ilovadagi boshqa chaqiruvlar bilan AYNI
 * (`['live-sessions']`) — ya'ni jadval, bosh sahifa va chat bitta
 * so'rovdan foydalanadi, to'rtta emas.
 *
 * ★ AVTOMATIK YANGILANISH: `SessionStarted` uchun real-vaqt push YO'Q
 * (`ILiveSessionNotifier` da faqat `SessionEndedAsync` bor). Shuning
 * uchun bu yerda YAGONA yo'l — sanoqli polling. 30 soniya tanlandi:
 * chat ro'yxati allaqachon shu oraliqda yangilanadi
 * (`GroupChatThreadList`), ya'ni yangi ritm kiritilmaydi.
 */
const POLL_MS = 30_000

export interface LiveGroups {
  /** Hozir jonli darsi bor guruh ID'lari. */
  ids: ComputedRef<ReadonlySet<number>>
  isLive: (groupId: number) => boolean
}

export function useLiveGroups(): LiveGroups {
  const query = useQuery({
    queryKey: ['live-sessions'],
    queryFn: ({ signal }) => fetchLiveSessions({ signal }),
    refetchInterval: POLL_MS,
  })

  const ids = computed<ReadonlySet<number>>(() => {
    const set = new Set<number>()

    for (const session of query.data.value ?? []) {
      // ⚠️ FAQAT SERVER HOLATI: bu yerda "boshlanish vaqti keldi" degan
      //    TAXMIN ishlatilmaydi (`useStudentSchedule` dagi `sessionState`
      //    shunday qiladi va bu o'quvchi uchun to'g'ri — u darsga oldinroq
      //    kirishi mumkin). Bu yerdagi savol boshqa: "ustoz darsni
      //    HAQIQATAN boshladimi". Vaqtga qarab bo'yalsa, ustoz kelmagan
      //    dars ham "jonli" bo'lib ko'rinardi.
      if (session.status === 'Live') set.add(session.groupId)
    }

    return set
  })

  return {
    ids,
    isLive: (groupId: number) => ids.value.has(groupId),
  }
}
