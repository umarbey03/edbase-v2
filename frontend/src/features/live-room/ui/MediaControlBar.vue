<script setup lang="ts">
import { AppIcon } from '@/shared/ui'

const props = withDefaults(
  defineProps<{
    isMicOn: boolean
    isCameraOn: boolean
    isScreenSharing: boolean
    /** Ekran ulashish faqat host uchun (SPEC: `roomAdmin` grant'i hostda). */
    canShareScreen: boolean
    handRaised: boolean
    isBusy?: boolean
    disabled?: boolean
    /** Mobil rejimda chat tugmasi ustidagi o'qilmagan xabarlar soni. */
    unreadCount?: number
  }>(),
  { isBusy: false, disabled: false, unreadCount: 0 },
)

const emit = defineEmits<{
  'toggle-mic': []
  'toggle-camera': []
  'toggle-screen': []
  'toggle-hand': []
  'toggle-chat': []
  leave: []
}>()

const BASE =
  'relative inline-flex size-11 items-center justify-center rounded-full transition-colors duration-150 disabled:cursor-not-allowed disabled:opacity-40'

/** Yoqilgan/o'chirilgan holat uchun uslub — takrorlanmasligi uchun bitta funksiya. */
function toneOf(active: boolean, activeTone = 'bg-ink-750 text-slate-100 hover:bg-ink-700'): string {
  return active ? activeTone : 'bg-rose-500/20 text-rose-300 hover:bg-rose-500/30'
}
</script>

<template>
  <div
    class="flex items-center justify-center gap-2 rounded-2xl bg-ink-900/90 px-3 py-2 ring-1 ring-inset ring-line backdrop-blur"
  >
    <button
      type="button"
      :class="[BASE, toneOf(props.isMicOn)]"
      :disabled="props.disabled || props.isBusy"
      :aria-pressed="props.isMicOn"
      :title="props.isMicOn ? 'Mikrofonni o‘chirish' : 'Mikrofonni yoqish'"
      @click="emit('toggle-mic')"
    >
      <AppIcon :name="props.isMicOn ? 'mic' : 'mic-off'" />
      <span class="sr-only">Mikrofon</span>
    </button>

    <button
      type="button"
      :class="[BASE, toneOf(props.isCameraOn)]"
      :disabled="props.disabled || props.isBusy"
      :aria-pressed="props.isCameraOn"
      :title="props.isCameraOn ? 'Kamerani o‘chirish' : 'Kamerani yoqish'"
      @click="emit('toggle-camera')"
    >
      <AppIcon :name="props.isCameraOn ? 'camera' : 'camera-off'" />
      <span class="sr-only">Kamera</span>
    </button>

    <button
      v-if="props.canShareScreen"
      type="button"
      :class="[
        BASE,
        props.isScreenSharing
          ? 'bg-brand-600 text-white hover:bg-brand-500'
          : 'bg-ink-750 text-slate-100 hover:bg-ink-700',
      ]"
      :disabled="props.disabled || props.isBusy"
      :aria-pressed="props.isScreenSharing"
      :title="props.isScreenSharing ? 'Ekran ulashishni to‘xtatish' : 'Ekranni ulashish'"
      @click="emit('toggle-screen')"
    >
      <AppIcon name="screen-share" />
      <span class="sr-only">Ekranni ulashish</span>
    </button>

    <button
      type="button"
      :class="[
        BASE,
        props.handRaised
          ? 'bg-amber-500 text-ink-950 hover:bg-amber-400'
          : 'bg-ink-750 text-slate-100 hover:bg-ink-700',
      ]"
      :disabled="props.disabled"
      :aria-pressed="props.handRaised"
      :title="props.handRaised ? 'Qo‘lni tushirish' : 'Qo‘l ko‘tarish'"
      @click="emit('toggle-hand')"
    >
      <AppIcon name="hand" />
      <span class="sr-only">Qo‘l ko‘tarish</span>
    </button>

    <!-- Mobil: chatni ochish -->
    <button
      type="button"
      :class="[BASE, 'bg-ink-750 text-slate-100 hover:bg-ink-700 lg:hidden']"
      title="Suhbat"
      @click="emit('toggle-chat')"
    >
      <AppIcon name="chat" />
      <span
        v-if="props.unreadCount > 0"
        class="absolute -right-0.5 -top-0.5 flex min-w-4 items-center justify-center rounded-full bg-brand-500 px-1 text-[10px] font-bold text-white"
        v-text="props.unreadCount > 99 ? '99+' : String(props.unreadCount)"
      />
      <span class="sr-only">Suhbat</span>
    </button>

    <div class="mx-1 h-6 w-px bg-line" aria-hidden="true" />

    <button
      type="button"
      :class="[BASE, 'bg-rose-600 text-white hover:bg-rose-500']"
      title="Darsdan chiqish"
      @click="emit('leave')"
    >
      <AppIcon name="leave" />
      <span class="sr-only">Chiqish</span>
    </button>
  </div>
</template>
