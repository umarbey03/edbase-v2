import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import type { ComputedRef } from 'vue'

import { fetchSessionRecordingStatus } from '@/entities/recording'

/**
 * ============================================================================
 *  🔴 "YOZUV KETMOQDA" — JONLI XONADAGI ROZILIK INDIKATORI
 * ============================================================================
 *
 * 2026-08-13 dan dars yozuvi AVTOMATIK boshlanadi (guruhning `recordEnabled`
 * kaliti). Eski tizimda ham shunday edi va uning ENG JIDDIY kamchiligi
 * aynan shu joyda edi: xonadagi hech kim — ustoz ham, o'quvchi ham —
 * yozib olinayotganini BILMASDI.
 *
 * v2 ning birinchi bosqichida yozuv qo'lda boshlanardi va host tugmasining
 * O'ZI indikator vazifasini bajarardi. Avtomatik rejimga o'tish o'sha
 * indikatorni yo'q qiladi, shuning uchun u ALOHIDA va HAMMAGA ko'rinadigan
 * element sifatida qayta qurildi.
 *
 * ── UCHTA QOIDA, UCHALASI HAM MAJBURIY ──────────────────────────────────
 *
 *  1) 🔴 HAQIQIY HOLATGA ULANADI, SOZLAMAGA EMAS. Manba —
 *     `GET .../recording-status`, ya'ni bazadagi yozuv qatori.
 *     `group.recordEnabled` ga ulash "yozilishi kerak" bilan "yozilyapti"
 *     ni aralashtirardi: egress yiqilgan darsda indikator yonib turib,
 *     hech qanday yozuv bo'lmasdi.
 *
 *  2) 🔴 SO'ROV DOIM YURADI, "yozuv ketayotganda" EMAS. Bu
 *     `useSessionRecording` dagi ro'yxat so'rovidan ATAYLAB farq qiladi
 *     (u faqat jarayon davom etayotganda takrorlanadi). Sabab oddiy:
 *     indikatorning butun vazifasi — yozuvning BOSHLANISHINI ko'rsatish.
 *     Faqat "yozuv bor" holatida so'rasak, u hech qachon `false` dan
 *     `true` ga o'ta olmasdi.
 *
 *  3) XATO YASHIRILADI, INDIKATOR ESA O'CHADI. Tarmoq uzilganda
 *     foydalanuvchiga yana bitta qizil chiziq ko'rsatish yordam bermaydi
 *     (xonada allaqachon aloqa banneri bor). Lekin "yozilmoqda" deb
 *     QOTIB QOLISH ham mumkin emas — shuning uchun xatoda indikator
 *     ko'rsatilmaydi.
 *     ⚠️ Bu 1-qoidaga zid emas: uzilgan aloqada biz haqiqatan HECH
 *     NARSA bilmaymiz, va "bilmayman" ni "ha" deb ko'rsatish indikatorga
 *     bo'lgan ishonchni yo'qotardi.
 */

/**
 * Holat shu oraliqda qayta so'raladi.
 *
 * ★ `useSessionRecording` dagi qiymat bilan AYNI (10 s) — ikki so'rov bir
 * xil ritmda yursin. ⚠️ Bu yerda u YUKGA ta'sir qiladi: so'rovni xonadagi
 * HAR ODAM yuboradi (host uchun bittagina emas). Shuning uchun javob
 * ataylab ikki maydondan iborat va bitta indeksli so'rovdan chiqadi
 * (backend izohi).
 */
const POLL_MS = 10_000

export interface UseRecordingIndicatorOptions {
  sessionId: number
  /** Dars jonli emas — so'rov yubormaymiz. */
  enabled: () => boolean
}

export interface UseRecordingIndicatorResult {
  /** Indikator ko'rsatilsinmi. */
  isRecording: ComputedRef<boolean>
  /** Yozuv haqiqatan boshlangan payt (ISO) — izoh matni uchun, bo'lmasligi mumkin. */
  startedAt: ComputedRef<string | null>
}

export function useRecordingIndicator(
  options: UseRecordingIndicatorOptions,
): UseRecordingIndicatorResult {
  const { sessionId } = options

  const statusQuery = useQuery({
    queryKey: ['session-recording-status', sessionId],
    queryFn: ({ signal }) => fetchSessionRecordingStatus(sessionId, { signal }),
    enabled: computed(() => options.enabled()),

    // 2-qoida: SHARTSIZ takrorlanadi (yuqoridagi izoh).
    refetchInterval: POLL_MS,

    /*
      Fon oynada ham so'raladi. Foydalanuvchi boshqa ilovaga o'tib qaytganda
      indikator ESKIRGAN bo'lishi mumkin emas — u ekranda "yozilmayapti"
      deb turgan holda yozuv allaqachon boshlangan bo'lardi.
    */
    refetchIntervalInBackground: true,

    /*
      `staleTime: 0` — kesh qaytarmasin. Xonaga qayta kirganda birinchi
      kadrdayoq HAQIQIY holat kerak.
    */
    staleTime: 0,
  })

  return {
    // 3-qoida: xato bo'lsa indikator YONMAYDI (`?? false`).
    isRecording: computed(() => statusQuery.data.value?.isRecording ?? false),
    startedAt: computed(() => statusQuery.data.value?.startedAt ?? null),
  }
}
