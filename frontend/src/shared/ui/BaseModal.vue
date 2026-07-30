<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'

import AppIcon from './AppIcon.vue'

/**
 * Modal oyna.
 *
 * TELEFONDA to'liq ekran (pastdan chiquvchi sheet), DESKTOPDA markazlashgan
 * oyna — 390px ekranda markazlashgan "kichik" oyna forma maydonlarini
 * siqib qo'yadi va klaviatura ochilganda kontent ko'rinmay qoladi.
 */
const props = withDefaults(
  defineProps<{
    open: boolean
    /** Bo'sh satr bo'lsa yuqori panel UMUMAN chizilmaydi (`sheet` bilan ishlatiladi). */
    title: string
    /** Kengroq oyna (jadval yoki ikki ustunli forma uchun). */
    wide?: boolean
    /**
     * "Varaq" ko'rinishi: DESKTOPDA HAM pastdan surilib chiqadi, yuqori
     * burchaklari 24px, kengligi 520px bilan cheklangan.
     *
     * O'quvchi ilovasi (Telegram Mini App) uchun kerak: eski `.modal` aynan
     * shunday edi va o'quvchilar profilni "pastdan chiqadigan varaq" deb
     * bilishadi. Xodim oynalari `sheet: false` bilan qoladi — ularning
     * ko'rinishiga TEGILMAGAN.
     */
    sheet?: boolean
  }>(),
  { wide: false, sheet: false },
)

const emit = defineEmits<{ close: [] }>()

const panel = ref<HTMLElement | null>(null)

/** Oyna yopilgach fokus qaysi elementga qaytishi kerakligini eslab qolamiz. */
let previouslyFocused: HTMLElement | null = null
/** Oyna ochiq paytda ostidagi sahifa skroll qilmasligi uchun. */
let savedBodyOverflow = ''

function handleKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.stopPropagation()
    emit('close')
  }
}

function lock(): void {
  previouslyFocused = document.activeElement instanceof HTMLElement ? document.activeElement : null
  savedBodyOverflow = document.body.style.overflow
  document.body.style.overflow = 'hidden'
  document.addEventListener('keydown', handleKeydown)
  void nextTick(() => panel.value?.focus())
}

function unlock(): void {
  document.removeEventListener('keydown', handleKeydown)
  document.body.style.overflow = savedBodyOverflow
  previouslyFocused?.focus()
  previouslyFocused = null
}

watch(
  () => props.open,
  (isOpen, wasOpen) => {
    if (isOpen) lock()
    else if (wasOpen === true) unlock()
  },
  { immediate: true },
)

// Komponent ochiq holatda yo'q qilinsa (masalan sahifa almashsa) — tozalaymiz,
// aks holda `body` skrolli abadiy qulflangan qoladi.
onBeforeUnmount(() => {
  if (props.open) unlock()
})
</script>

<template>
  <Teleport to="body">
    <div
      v-if="props.open"
      class="fixed inset-0 z-50 flex bg-black/65 backdrop-blur-sm"
      :class="
        props.sheet
          ? 'items-end justify-center'
          : 'sm:items-center sm:justify-center sm:p-5'
      "
      role="presentation"
      @click.self="emit('close')"
    >
      <div
        ref="panel"
        class="flex max-h-dvh w-full animate-sheet-up flex-col overflow-hidden border-line bg-ink-900"
        :class="
          props.sheet
            ? 'max-w-[520px] rounded-t-3xl border-x border-t'
            : [
              'sm:max-h-[92dvh] sm:animate-fade-up sm:rounded-2xl sm:border',
              props.wide ? 'sm:max-w-3xl' : 'sm:max-w-lg',
            ]
        "
        role="dialog"
        aria-modal="true"
        tabindex="-1"
      >
        <header
          v-if="props.title.length > 0"
          class="flex shrink-0 items-center gap-3 border-b border-line px-4 py-3 sm:px-6 sm:py-4"
        >
          <h2
            class="min-w-0 flex-1 truncate text-[15px] font-semibold"
            v-text="props.title"
          />
          <button
            type="button"
            class="tap-target -mr-2 flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
            title="Yopish"
            @click="emit('close')"
          >
            <AppIcon
              name="close"
              :size="18"
            />
          </button>
        </header>

        <!--
          `sheet` da pastki padding safe-area'ni hisobga oladi: iPhone'dagi
          "home indicator" varaqning oxirgi tugmasini yopib qo'ymasin.
        -->
        <div
          class="scrollbar-slim min-h-0 flex-1 overflow-y-auto px-4 py-4 sm:px-6 sm:py-5"
          :style="
            props.sheet ? { paddingBottom: 'calc(1.5rem + env(safe-area-inset-bottom, 0px))' } : {}
          "
        >
          <slot />
        </div>

        <footer
          v-if="$slots.footer"
          class="flex shrink-0 flex-col-reverse gap-2 border-t border-line px-4 py-3 sm:flex-row sm:justify-end sm:px-6 sm:py-4"
        >
          <slot name="footer" />
        </footer>
      </div>
    </div>
  </Teleport>
</template>
