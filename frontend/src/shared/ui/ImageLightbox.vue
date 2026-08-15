<script setup lang="ts">
import { ref } from 'vue'

import { useModalHost } from '@/shared/lib/useModalHost'

import AppIcon from './AppIcon.vue'

/**
 * ============================================================================
 *  RASMNI KATTALASHTIRISH — TELEGRAM NAQSHI (2026-08-15)
 * ============================================================================
 *
 * Loyiha egasi: *"rasmlarni ko'rishga bosganda ularning orqasi butunlay oq
 * bo'lib qolyapti. Telegramnikiga o'xshab faqat rasm qismi orqa fonni
 * berkitsin, qolgan orqa fon biroz shaffofroq bo'lib tursin"*.
 *
 * ── NIMA UCHUN `BaseModal` YARAMADI ────────────────────────────────────────
 *
 * Ilgari bu yerda `BaseModal wide` ishlatilardi. U — HUJJAT oynasi: OQ
 * kartochka, sarlavha paneli ("Rasm") va chekinishlar. Rasm uchun bu
 * butunlay noto'g'ri qobiq:
 *
 *   • oq kartochka rasmdan KATTAROQ bo'lib, ekranni oq bilan to'ldirardi
 *     (aynan shikoyat qilingan holat);
 *   • sarlavha paneli hech qanday ma'lumot bermasdi ("Rasm" — buni
 *     foydalanuvchi allaqachon ko'rib turibdi);
 *   • rasm chekinishlar ichida qolib, ekranning yarmigacha kichrayardi.
 *
 * ── QANDAY ISHLAYDI ────────────────────────────────────────────────────────
 *
 * Butun ekran TO'Q parda (`slate-950/85`) + yengil blur bilan qoplanadi,
 * rasm esa markazda o'z nisbatida chiziladi. Parda TO'LIQ QORA EMAS:
 * 85% — ostidagi suhbat sezilib turadi, ya'ni foydalanuvchi "qayerdan
 * kelganini" yo'qotmaydi (Telegram ham shunday qiladi).
 *
 * Yopish: rasmdan TASHQARIGA bosish, ✕ tugmasi yoki ESC.
 *
 * ★ `useModalHost` — skroll qulfi, fokus tuzog'i va ESC steki shundan.
 * O'z `keydown` ishlovchisini yozish TAQIQLANGAN (`useModalHost` izohi):
 * bu oyna suhbat ustida, suhbat esa ba'zan boshqa oyna ustida ochiladi.
 */
const props = defineProps<{
  /** Ko'rsatiladigan rasm manzili. `null` — oyna yopiq. */
  src: string | null
  /** Ekran o'qigich uchun tavsif. */
  alt?: string
}>()

const emit = defineEmits<{ close: [] }>()

const panel = ref<HTMLElement | null>(null)

useModalHost({
  open: () => props.src !== null,
  onClose: () => emit('close'),
  panel,
  kind: 'dialog',
})
</script>

<template>
  <Teleport to="body">
    <div
      v-if="props.src !== null"
      ref="panel"
      class="fixed inset-0 z-[60] flex animate-fade-up items-center justify-center bg-slate-950/85 p-4 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-label="Rasm"
      tabindex="-1"
      @click.self="emit('close')"
    >
      <!--
        ★ `z-[60]` — `BaseModal`/`ConfirmDialog` (z-50) DAN YUQORI: rasm
        oynadan (masalan vazifa tekshirish oynasidan) ham ochilishi mumkin
        va u o'sha oynaning USTIDA turishi kerak.
      -->
      <img
        :src="props.src"
        :alt="props.alt ?? 'Kattalashtirilgan rasm'"
        class="max-h-[92dvh] max-w-full rounded-lg object-contain shadow-2xl"
      >

      <!--
        Yopish tugmasi — ekranning yuqori o'ng burchagida, rasmdan
        MUSTAQIL. Rasm ichiga qo'yilsa, baland surat uni ekrandan
        chiqarib yuborardi.

        `top` da `env(safe-area-inset-top)`: "tirnoqli" iPhone'da tugma
        tizim paneli ostiga tushib qolmasin.
      -->
      <button
        type="button"
        class="tap-target absolute right-3 flex size-10 items-center justify-center rounded-full bg-slate-950/50 text-white backdrop-blur-sm transition-colors hover:bg-slate-950/70"
        style="top: calc(0.75rem + env(safe-area-inset-top, 0px))"
        aria-label="Yopish"
        @click="emit('close')"
      >
        <AppIcon
          name="close"
          :size="20"
        />
      </button>
    </div>
  </Teleport>
</template>
