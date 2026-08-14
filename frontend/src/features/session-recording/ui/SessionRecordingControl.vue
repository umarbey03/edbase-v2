<script setup lang="ts">
import { computed } from 'vue'

import { recordingStatusLabel } from '@/entities/recording'
import { AppIcon, BaseButton } from '@/shared/ui'

import { useSessionRecording } from '../model/useSessionRecording'

/**
 * Jonli darsdagi "Yozuvni boshlash / to'xtatish" tugmasi.
 *
 * ══════════════════════════════════════════════════════════════════════
 * ★ 2026-08-13 DAN BU TUGMA — OVERRIDE, ASOSIY YO'L EMAS.
 * ══════════════════════════════════════════════════════════════════════
 *
 * Yozuv endi AVTOMATIK boshlanadi: guruhda `recordEnabled` yoqilgan bo'lsa,
 * dars boshlanishi bilan navbatga tushadi (backend:
 * `LiveSessionService.StartAsync` → `IAutoRecordingScheduler`). Ya'ni odatiy
 * holatda ustoz xonaga kirganda tugma allaqachon "Yozuvni to'xtatish" deb
 * turadi va hech narsa bosish shart emas.
 *
 * ── NIMA UCHUN TUGMA SAQLANDI (o'chirilmadi) ──────────────────────────
 *
 *  1) 🔴 TO'XTATISH — ROZILIKNING YAGONA CHIQISHI. Yozuv o'z-o'zidan
 *     boshlangani uchun "bu darsni yozmang" deyishning boshqa yo'li YO'Q.
 *     Tugmani olib tashlash darsni tugatmasdan yozuvni to'xtatishni
 *     butunlay imkonsiz qilardi (va `stopRecording` chaqiruvchisiz
 *     qolardi).
 *  2) Guruhda yozuv O'CHIQ, lekin AYNAN shu darsni yozib olish kerak
 *     (ochiq dars, o'rnini bosuvchi ustoz).
 *  3) Dars boshlanganda ombor/LiveKit sozlanmagan edi va avtomatik navbat
 *     qator qo'shmadi. Administrator paneldan sozlamani tuzatgach, ustoz
 *     darsni QAYTA BOSHLAMASDAN yozuvni yoqa oladi.
 *
 * ⚠️ TUGMA INDIKATOR EMAS. Ilgari u ikkala vazifani ham bajarardi va
 * aynan shu rozilik dalilining zaif joyi edi: uni FAQAT host ko'rardi.
 * Endi indikator alohida (`RecordingIndicator`) va xonadagi HAR KIMGA —
 * o'quvchiga ham — ko'rinadi.
 *
 * KO'RINISH SHARTI ota komponentda (`canManageSession && isLive`): o'quvchi
 * bu chaqiruvlardan **403** oladi (jonli tekshirilgan), ya'ni tugma unga
 * umuman chizilmaydi.
 */
const props = defineProps<{
  sessionId: number
  /** Dars jonli emas — so'rov yubormaymiz (server 409 berardi). */
  isLive: boolean
}>()

const recording = useSessionRecording({
  sessionId: props.sessionId,
  enabled: () => props.isLive,
})

const isRecording = computed(() => recording.activeRecording.value !== null)

const label = computed(() => {
  const active = recording.activeRecording.value
  if (active === null) return 'Yozuvni boshlash'
  // "Navbatda"/"Boshlanmoqda" holatida "To'xtatish" yozish chalg'itardi —
  // xodim yozuv allaqachon ketyapti deb o'ylardi.
  return active.status === 'Active' ? 'Yozuvni to‘xtatish' : recordingStatusLabel(active.status)
})

function toggle(): void {
  if (isRecording.value) recording.stop()
  else recording.start()
}
</script>

<template>
  <!--
    `title` o'rovchi `<span>` da: komponentning ildizi bittadan ko'p (tugma +
    `Teleport`), shuning uchun atributlar `BaseButton` ga o'z-o'zidan
    o'tmaydi. Telefonda yorliq matni yashiringani uchun izoh SHART.
  -->
  <span
    class="inline-flex"
    :title="label"
  >
    <BaseButton
      size="sm"
      :variant="isRecording ? 'danger' : 'secondary'"
      :loading="recording.isBusy.value"
      @click="toggle"
    >
      <template #icon>
        <AppIcon
          name="camera"
          :size="14"
        />
      </template>
      <span class="hidden sm:inline">{{ label }}</span>
    </BaseButton>
  </span>

  <!--
    Xato AYNAN tugma yonida ko'rsatiladi: 409 ("Avval darsni boshlang"),
    403 (qarz/ruxsat) va 503 (ombor sozlanmagan) — uchalasi ham xodim
    darhol o'qishi kerak bo'lgan matnlar. `toUserMessage` server `detail` ini
    o'zgarishsiz beradi.
  -->
  <Teleport to="body">
    <!--
      ★ `bottom` inline `style` da: toast `<body>` ga teleport qilingani uchun
      jonli xona ildizidagi xavfsiz zona padding'i unga TA'SIR QILMAYDI.
      iPhone'da 12px lik `bottom-3` xabarni "home indicator" ostiga tushirib,
      matnning pastki qatorini va yopish tugmasini qiyin bosiladigan qilardi.
      `env()` ni Tailwind klassi bilan berish uchun `style.css` ga utility
      qo'shish kerak edi — ilovadagi mavjud naqsh ham inline `style`
      (`BaseModal`, `BaseDrawer`, `StudentTabBar`).
    -->
    <div
      v-if="recording.actionError.value !== null"
      class="fixed inset-x-3 z-50 mx-auto max-w-md rounded-xl border border-rose-500/30 bg-rose-950/95 px-4 py-3 text-xs text-rose-100 shadow-xl"
      :style="{ bottom: 'calc(0.75rem + env(safe-area-inset-bottom, 0px))' }"
      role="alert"
    >
      <div class="flex items-start gap-2">
        <span
          class="flex-1 leading-relaxed"
          v-text="recording.actionError.value"
        />
        <button
          type="button"
          class="tap-expand shrink-0 rounded p-0.5 hover:text-rose-50"
          title="Yopish"
          @click="recording.actionError.value = null"
        >
          <AppIcon
            name="close"
            :size="14"
          />
        </button>
      </div>
    </div>
  </Teleport>
</template>
