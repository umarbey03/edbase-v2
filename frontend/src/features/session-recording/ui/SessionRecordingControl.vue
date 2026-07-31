<script setup lang="ts">
import { computed } from 'vue'

import { recordingStatusLabel } from '@/entities/recording'
import { AppIcon, BaseButton } from '@/shared/ui'

import { useSessionRecording } from '../model/useSessionRecording'

/**
 * Jonli darsdagi "Yozuvni boshlash / to'xtatish" tugmasi.
 *
 * ★ ESKI ILOVADA BUNDAY TUGMA YO'Q EDI. U yerda yozuv GURUH SOZLAMASI edi
 * ("Darslarni yozib olish (recording)" — `academic.html`, 1646 va 1703-qatorlar)
 * va dars yakunlanganda o'zi to'xtardi (`live.html`, 690-qator: "davomat
 * yopiladi, yozuv to'xtaydi"). v2 backendi esa QO'LDA boshlash/to'xtatish
 * endpointlarini beradi, shuning uchun boshqaruv ustozga ko'rinadigan joyga —
 * jonli xonaning yuqori paneliga qo'yildi.
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
    <div
      v-if="recording.actionError.value !== null"
      class="fixed inset-x-3 bottom-3 z-50 mx-auto max-w-md rounded-xl border border-rose-500/30 bg-rose-950/95 px-4 py-3 text-xs text-rose-100 shadow-xl"
      role="alert"
    >
      <div class="flex items-start gap-2">
        <span
          class="flex-1 leading-relaxed"
          v-text="recording.actionError.value"
        />
        <button
          type="button"
          class="shrink-0 rounded p-0.5 hover:text-rose-50"
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
