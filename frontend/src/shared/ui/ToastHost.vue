<script setup lang="ts">
import { dismissToast, useToasts } from '@/shared/lib/useToast'
import type { ToastTone } from '@/shared/lib/useToast'

import AppIcon from './AppIcon.vue'
import type { IconName } from './icon-names'

/**
 * Toast navbatini chizuvchi host.
 *
 * 🔴 ILOVADA BITTA BO'LADI — `App.vue` ga qo'yilgan (`ConfirmHost` bilan
 * AYNI qoida). Ikkinchisi qo'yilsa har xabar IKKI marta chizilardi:
 * ikkalasi ham bir xil modul holatiga qaraydi.
 *
 * ── JOYLASHUV (loyiha egasining talabi) ────────────────────────────────
 *
 *   • TELEFON  — tepada, MARKAZDA. Barmoq ekranning pastida bo'ladi,
 *     ya'ni tepadagi xabar hech narsani to'smaydi; markaz esa tor
 *     ekranda yagona muvozanatli joy.
 *   • ≥640px  — tepada, O'NG tomonda. Keng ekranda markazdagi xabar
 *     kontentning eng qimmat qismini (o'rtasini) yopadi va u yerda
 *     modal oynalar ochiladi.
 *
 * ★ `env(safe-area-inset-top)` — "tirnoqli" iPhone'da xabar tizim
 * paneli ostiga tushib qolmasin.
 *
 * ★ `pointer-events-none` KONTEYNERDA, `pointer-events-auto` esa
 * XABARNING O'ZIDA: konteyner butun kenglikni egallaydi va usiz u
 * ostidagi tugmalarni ko'rinmas holda bloklab qo'yardi.
 */
const toasts = useToasts()

/*
  RANG JUFTLIKLARI `BaseBadge` DAN OLINDI — ular `scripts/contrast-audit.mjs`
  da allaqachon tekshirilgan (pastel tint + to'q matn). Yangi juftlik
  kiritilmadi, ya'ni auditni qayta yurgizish shart emas.

  ⚠️ ESLATMA: bu faylda shkalalar TESKARI (`style.css` bosh izohi) —
  `-200` MATN uchun to'q rang, `-500` esa tint asosi. "Juda to'q" ko'ringan
  qiymatni "tuzatmang".
*/
const TONES: Record<ToastTone, string> = {
  success: 'border-green-500/25 bg-green-950/95 text-green-200',
  error: 'border-rose-500/25 bg-rose-950/95 text-rose-200',
  warning: 'border-amber-500/25 bg-amber-950/95 text-amber-200',
  /*
    ⚠️ `bg-brand-950` YO'Q VA BO'LMAYDI: brend shkalasi 900 da tugaydi va
    uning 800/900 darajalari — TO'Q navy (matn uchun), rose/green dagi
    kabi PASTEL SIRT emas. Shuning uchun `info` toni kartochka sirtini
    (`ink-900`) oladi, matn esa auditdan o'tgan `brand-300`.
  */
  info: 'border-brand-500/25 bg-ink-900/95 text-brand-300',
}

const ICON_TONES: Record<ToastTone, string> = {
  success: 'text-green-500',
  error: 'text-rose-500',
  warning: 'text-amber-500',
  info: 'text-brand-500',
}

const ICONS: Record<ToastTone, IconName> = {
  success: 'check',
  error: 'alert',
  warning: 'alert',
  info: 'bell',
}
</script>

<template>
  <Teleport to="body">
    <div
      class="pointer-events-none fixed inset-x-3 z-[200] flex flex-col items-center gap-2 sm:inset-x-auto sm:right-4 sm:items-end"
      style="top: calc(0.75rem + env(safe-area-inset-top, 0px))"
      role="status"
      aria-live="polite"
    >
      <!--
        ★ `TransitionGroup` — xabar QO'SHILGANDA ham, YO'QOLGANDA ham
        animatsiya bo'lsin. Oddiy `v-for` da yo'qolish bir zumda sodir
        bo'lardi va ko'z uni "chaqnash" deb qabul qilardi.
      -->
      <TransitionGroup
        enter-active-class="transition duration-200 ease-out"
        enter-from-class="-translate-y-2 opacity-0"
        enter-to-class="translate-y-0 opacity-100"
        leave-active-class="transition duration-150 ease-in absolute"
        leave-from-class="opacity-100"
        leave-to-class="opacity-0"
        move-class="transition duration-200"
      >
        <button
          v-for="toast in toasts"
          :key="toast.id"
          type="button"
          class="pointer-events-auto flex w-full max-w-[min(26rem,100%)] items-start gap-2.5 rounded-2xl border px-3.5 py-2.5 text-left shadow-lg backdrop-blur-sm sm:w-auto sm:min-w-[16rem]"
          :class="TONES[toast.tone]"
          aria-label="Xabarni yopish"
          @click="dismissToast(toast.id)"
        >
          <AppIcon
            :name="ICONS[toast.tone]"
            :size="16"
            class="mt-px shrink-0"
            :class="ICON_TONES[toast.tone]"
          />
          <span
            class="min-w-0 flex-1 text-[13px] font-medium leading-snug"
            v-text="toast.text"
          />
        </button>
      </TransitionGroup>
    </div>
  </Teleport>
</template>
