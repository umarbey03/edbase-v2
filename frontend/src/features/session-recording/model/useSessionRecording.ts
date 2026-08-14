import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'
import type { ComputedRef, Ref } from 'vue'

import { fetchSessionRecordings, isRecordingInProgress, startRecording, stopRecording } from '@/entities/recording'
import type { Recording } from '@/entities/recording'
import { toUserMessage } from '@/shared/api'
import { useConfirm } from '@/shared/lib/useConfirm'

/**
 * ============================================================================
 *  BITTA DARSNING YOZUVLARI + BOSHLASH/TO'XTATISH
 * ============================================================================
 *
 * ★ RUXSAT — JONLI TEKSHIRILGAN, TAXMIN QILINMAGAN:
 *   • `POST .../recordings/start` va `.../stop`:
 *       — o'quvchi: **403** (tanasiz javob);
 *       — darsni olib boradigan ustoz/kurator: ruxsat bor;
 *       — o'quv bo'limi/admin: ruxsat bor.
 *     Dars JONLI bo'lmasa ikkalasi ham **409** beradi ("Yozuvni faqat JONLI
 *     dars uchun boshlash mumkin. Avval darsni boshlang." / "Bu darsda faol
 *     yozuv yo'q.").
 *   • `GET .../recordings`: dars EGASI bo'lmagan ustoz **403**, guruhda
 *     bo'lmagan o'quvchi **403**, guruh a'zosi va admin **200**.
 * Shu sababli tugmalar FAQAT dars egasiga/boshqaruvchiga ko'rsatiladi, lekin
 * ro'yxat hammaga so'raladi — serverning o'z qoidasi takrorlanmaydi, faqat
 * ko'rinmaydigan tugma bosilib 403 chiqmasligi ta'minlanadi.
 */

/** Yozuv ketayotganda holat shu oraliqda qayta so'raladi. */
const ACTIVE_POLL_MS = 10_000

export interface UseSessionRecordingOptions {
  sessionId: number
  /** Dars jonli emasligida so'rov yubormaslik uchun. */
  enabled?: () => boolean
}

export interface UseSessionRecordingResult {
  recordings: ComputedRef<Recording[]>
  /** Hozir ketayotgan yozuv (bo'lmasa `null`). */
  activeRecording: ComputedRef<Recording | null>
  isPending: ComputedRef<boolean>
  isFetching: ComputedRef<boolean>
  listError: ComputedRef<string | null>
  /** Boshlash/to'xtatish xatosi — foydalanuvchi yopgunicha turadi. */
  actionError: Ref<string | null>
  isBusy: ComputedRef<boolean>
  start: () => void
  stop: () => void
  refetch: () => void
}

export function useSessionRecording(
  options: UseSessionRecordingOptions,
): UseSessionRecordingResult {
  const { sessionId } = options
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const actionError = ref<string | null>(null)

  const listQuery = useQuery({
    queryKey: ['session-recordings', sessionId],
    queryFn: ({ signal }) => fetchSessionRecordings(sessionId, { signal }),
    enabled: computed(() => (options.enabled === undefined ? true : options.enabled())),
    /*
      Yozuvning `Requested` -> `Active` -> `Completed` yo'li FONDA kechadi
      (egress webhook'i serverga keladi), klientga hodisa yuborilmaydi.
      Shuning uchun faqat shu holatlarda so'rov takrorlanadi.
    */
    refetchInterval: (query) =>
      (query.state.data ?? []).some(isRecordingInProgress) ? ACTIVE_POLL_MS : false,
  })

  const recordings = computed(() => listQuery.data.value ?? [])

  /**
   * Faol yozuv. `Requested`/`Starting` ham "faol" hisoblanadi: aks holda
   * xodim "Yozuvni boshlash" ni ikki marta bosib, ikkita egress ochib
   * qo'yishi mumkin edi.
   */
  const activeRecording = computed(
    () => recordings.value.find(isRecordingInProgress) ?? null,
  )

  function refresh(): void {
    void queryClient.invalidateQueries({ queryKey: ['session-recordings', sessionId] })
    // Umumiy ro'yxat ham eskiradi (o'quv bo'limi sahifasi ochiq bo'lishi mumkin).
    void queryClient.invalidateQueries({ queryKey: ['recordings', 'list'] })
  }

  const startMutation = useMutation({
    mutationFn: () => startRecording(sessionId),
    onSuccess: (recording) => {
      /*
        ⚠️ 200 — YOZUV BOSHLANDI DEGANI EMAS. Egress rad etsa server AYNAN
        shu javobda `status: "Requested"` va `error: "Yozuv xizmati rad etdi: …"`
        yuboradi (jonli ko'rilgan). Xabarni yashirmaymiz — aks holda xodim
        yozuv ketyapti deb o'ylab, dars oxirida hech narsa topmasdi.
      */
      actionError.value = recording.error
      refresh()
    },
    onError: (error: unknown) => {
      actionError.value = toUserMessage(error)
    },
  })

  const stopMutation = useMutation({
    mutationFn: () => stopRecording(sessionId),
    onSuccess: () => {
      actionError.value = null
      refresh()
    },
    onError: (error: unknown) => {
      actionError.value = toUserMessage(error)
    },
  })

  return {
    recordings,
    activeRecording,
    isPending: computed(() => listQuery.isPending.value),
    isFetching: computed(() => listQuery.isFetching.value),
    listError: computed(() =>
      listQuery.error.value !== null ? toUserMessage(listQuery.error.value) : null,
    ),
    actionError,
    isBusy: computed(() => startMutation.isPending.value || stopMutation.isPending.value),
    /*
      ★ BOSHLASHDA TASDIQ SO'RALMAYDI — ATAYLAB (R4).

      Yozuv 2026-08-13 dan AVTOMATIK boshlanadi, ya'ni bu tugma "boshlash"
      holatida faqat TUZATISH yo'li bo'lib qoladi: guruhda yozuv o'chiq
      edi, yoki dars boshlanganda ombor sozlanmagan edi (izohi
      `SessionRecordingControl.vue` da). Bunday yo'lni tasdiq oynasi bilan
      og'irlashtirish — allaqachon nosozlikni tuzatayotgan xodimga
      qo'shimcha qadam. Amal esa qaytariladigan: bosib yuborilsa darhol
      to'xtatiladi.
    */
    start: () => {
      actionError.value = null
      startMutation.mutate()
    },

    /**
     * 🔴 TO'XTATISH TASDIQLANADI — U ROZILIKNING YAGONA CHIQISHI VA
     * QAYTARIB BO'LMAYDI.
     *
     * Egress yozuvni to'xtatgach fayl YOPILADI. "Davom ettirish" degan
     * amal yo'q: qayta boshlansa YANGI fayl ochiladi va bitta darsdan
     * ikkita bo'lak qoladi. Ya'ni xato bosish qaytarilmaydi — darsning
     * o'rtasi bir faylda tugab, ikkinchisi boshqasida boshlanadi.
     *
     * ★ TASDIQ AYNAN SHU YERDA (tugmada emas): `stop()` — modeldagi
     * YAGONA to'xtatish yo'li. Tugmaga qo'yilsa, ertaga ikkinchi
     * chaqiruvchi (masalan "darsdan chiqish" oqimi) uni chetlab o'tardi.
     *
     * `void (async …)()` — qaytish turi `() => void` bo'lib qoladi: uni
     * shablonda `@click="recording.stop"` bilan chaqirish naqshi
     * o'zgarmasin (`UseSessionRecordingResult` tashqi shartnoma).
     */
    stop: () => {
      void (async (): Promise<void> => {
        const ok = await confirm({
          title: 'Yozuvni to‘xtatish',
          message: 'Dars yozuvi to‘xtatiladi va shu paytgacha yozilgani yakuniy fayl bo‘lib yopiladi.',
          confirmLabel: 'To‘xtatish',
          tone: 'warning',
          details: [
            'Darsning qolgan qismi umuman yozilmaydi.',
            'Yozuv avtomatik boshlangan — to‘xtatilgach o‘z-o‘zidan qayta boshlanmaydi.',
            'Qayta yoqilsa YANGI fayl ochiladi: bitta darsdan ikkita alohida yozuv qoladi.',
          ],
        })
        if (!ok) return

        actionError.value = null
        stopMutation.mutate()
      })()
    },
    refetch: () => {
      void listQuery.refetch()
    },
  }
}
