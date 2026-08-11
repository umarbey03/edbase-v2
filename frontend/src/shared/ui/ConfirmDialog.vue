<script setup lang="ts">
import { computed, ref, useId } from 'vue'

import type { ConfirmTone } from '@/shared/lib/useConfirm'
import { MODAL_AUTOFOCUS_CLASS, useModalHost } from '@/shared/lib/useModalHost'

import AppIcon from './AppIcon.vue'
import BaseButton from './BaseButton.vue'
import type { IconName } from './icon-names'

/**
 * UMUMIY TASDIQLASH OYNASI.
 *
 * Talab: *"Platformadagi har qanday edit, delete, change qilingan ma'lumotlar
 * tasdiqlashni so'rashi kerak"*.
 *
 * Bu komponent — KO'RINISH. Chaqiruv `shared/lib/useConfirm.ts` (imperativ,
 * `await confirm({...})`) orqali bo'ladi va u `ConfirmHost` ni boshqaradi.
 * Deklarativ ishlatish ham mumkin (`:open` + `@confirm`/`@cancel`), lekin
 * mavjud oqimlarga bitta qator bilan qo'shiladigan yo'l — `useConfirm`.
 *
 * NEGA `BaseModal` USTIGA QURILMAGAN: bu oynaning ROLI boshqa —
 * `role="alertdialog"`, `aria-describedby`, ton ikonkasi, tafsilotlar
 * ro'yxati va tonga qarab o'zgaradigan boshlang'ich fokus. `BaseModal` ga
 * bularni qo'shsak u yana bir shart daraxti bilan o'sardi.
 *
 * (Tarixiy izoh: ilgari bu yerda "`BaseModal` skroll qulfi va ESC ni o'zi
 * boshqaradi" deb yozilgan edi — 2026-08-11 da `BaseModal` ham
 * `useModalHost` ga o'tkazildi, ya'ni endi UCHALASI bir mexanikada va
 * ESC har doim faqat eng ustidagi qatlamni yopadi.)
 *
 * NEGA `window.confirm` EMAS — sabab `ConfirmDeleteDialog.vue` izohida:
 * brauzer oynasi yopilgach server xatosini (409 sababi) ko'rsatadigan joy
 * qolmaydi. `ConfirmDeleteDialog` shu sabab SAQLANADI: u xato kelganda ochiq
 * turadi. Bu oyna esa `boolean` qaytaradi va DARHOL yopiladi — xato mavjud
 * xato ko'rsatish mexanizmi (toast / `DataStatus`) orqali chiqadi.
 *
 * ⚠️ `ConfirmTone` turi `shared/lib/useConfirm.ts` da yashaydi: `<script setup>`
 * blokidan TUR EKSPORT QILIB BO'LMAYDI (`icon-names.ts` izohidagi bilan bir xil
 * sabab), va tur baribir imperativ API bilan birga ishlatiladi.
 */
const props = withDefaults(
  defineProps<{
    open: boolean
    title: string
    message: string
    /** Tasdiq tugmasi matni. */
    confirmLabel?: string
    cancelLabel?: string
    /** Amal og'irligi: ikonka rangi va tugma variantini belgilaydi. */
    tone?: ConfirmTone
    /**
     * Qo'shimcha tafsilotlar ro'yxati — "nima o'zgaradi / nima saqlanadi".
     * Reja B2: `warning` tonida RAQAMLAR bilan ko'rsatilishi shart
     * (masalan "+3 dars qo'shiladi, −1 dars o'chadi").
     */
    details?: readonly string[]
    /** Tasdiq tugmasidagi yuklanish holati (deklarativ ishlatishda). */
    pending?: boolean
  }>(),
  {
    confirmLabel: 'Tasdiqlash',
    cancelLabel: 'Bekor qilish',
    tone: 'primary',
    details: () => [],
    pending: false,
  },
)

const emit = defineEmits<{ confirm: []; cancel: [] }>()

const panel = ref<HTMLElement | null>(null)
const titleId = useId()
const messageId = useId()

const TONE_ICONS: Record<ConfirmTone, IconName> = {
  danger: 'alert',
  warning: 'alert',
  primary: 'check',
}

/*
  Ikonka doirasi. Pastel to'ldirish + to'q ikonka — `BaseBadge` bilan bir
  uslubda (`bg-{tone}/12` + to'qroq matn).

  🔴 `warning` da ikonka `amber-400`, `amber-500` EMAS (2026-08-11 tuzatildi).
  `text-amber-500` (#f79009) o'z 12% tinti ustida faqat 2.12:1 beradi va
  20px ogohlantirish ikonkasi GRAFIK element — WCAG 1.4.11 bo'yicha 3:1
  kerak. `amber-400` (#b54708) bilan 5.06:1.

  Nega `danger`/`primary` da 500 QOLADI: `rose-500` o'z tinti ustida 4.03:1,
  `brand-500` 4.97:1 — ikkisi ham talabga mos. Faqat SARIQ fizik jihatdan
  yetmaydi (`style.css` dagi amber izohi), shuning uchun istisno bittada.
*/
const TONE_CIRCLES: Record<ConfirmTone, string> = {
  danger: 'bg-rose-500/12 text-rose-500',
  warning: 'bg-amber-500/12 text-amber-400',
  primary: 'bg-brand-500/12 text-brand-500',
}

/*
  Tasdiq tugmasining varianti tonga qarab.

  ✅ `warning` endi HAQIQATAN amber (2026-08-11): `BaseButton` ga `warning`
  varianti qo'shildi. Ilgari bu yerda `primary` (indigo) turardi, chunki
  amber variant yo'q edi — natijada "yon ta'siri katta" amal (jadval qayta
  generatsiyasi, +N/−N dars) oddiy saqlash bilan BIR XIL ko'rinardi va
  reja B2 dagi uch daraja amalda IKKI darajaga tushib qolgan edi.
*/
const TONE_BUTTONS: Record<ConfirmTone, 'danger' | 'warning' | 'primary'> = {
  danger: 'danger',
  warning: 'warning',
  primary: 'primary',
}

/*
  BOSHLANG'ICH FOKUS.
  • `danger`/`warning` — "Bekor qilish" da: xato bosishdan himoya (Enter yoki
    Space avtomatik ravishda XAVFSIZ variantni tanlaydi);
  • `primary` (odatdagi saqlash) — "Tasdiqlash" da: bu oqim kunda o'nlab marta
    takrorlanadi va har safar Tab bosish ish tezligini yeydi.

  ⚠️ Topshiriqda "Enter = tasdiq" va "fokus Bekor qilishda" deyilgan — ular
  bir-biriga qarshi (fokus tugmada bo'lsa Enter O'SHA tugmani bosadi).
  Yuqoridagi taqsimot ikkalasining maqsadini saqlaydi; `danger` da Enter
  ataylab BEKOR qiladi.
*/
const cancelClass = computed(() => (props.tone === 'primary' ? '' : MODAL_AUTOFOCUS_CLASS))
const confirmClass = computed(() => (props.tone === 'primary' ? MODAL_AUTOFOCUS_CLASS : ''))

useModalHost({
  open: () => props.open,
  onClose: () => emit('cancel'),
  panel,
  kind: 'dialog',
})

/**
 * Enter — tasdiq, LEKIN faqat fokus tugmada BO'LMAGANDA. Aks holda ikki amal
 * bir vaqtda ishga tushardi: brauzer fokuslangan tugmani "bosadi", biz esa
 * tasdiqni yuborardik.
 */
function onEnter(event: KeyboardEvent): void {
  if (props.pending) return
  const target = event.target
  if (target instanceof HTMLElement && target.closest('button,a,input,select,textarea') !== null) {
    return
  }
  emit('confirm')
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="props.open"
      class="fixed inset-0 z-50 flex items-end justify-center bg-slate-900/35 backdrop-blur-sm sm:items-center sm:p-5"
      role="presentation"
      @click.self="emit('cancel')"
    >
      <div
        ref="panel"
        class="flex max-h-dvh w-full animate-sheet-up flex-col overflow-hidden border-line bg-ink-900 shadow-lg max-sm:rounded-t-3xl max-sm:border-x max-sm:border-t sm:max-h-[92dvh] sm:max-w-md sm:animate-fade-up sm:rounded-[1.25rem] sm:border"
        role="alertdialog"
        aria-modal="true"
        :aria-labelledby="titleId"
        :aria-describedby="messageId"
        tabindex="-1"
        @keydown.enter="onEnter"
      >
        <div
          class="scrollbar-slim min-h-0 flex-1 overflow-y-auto px-4 py-5 sm:px-6"
          :style="{ paddingBottom: 'calc(1.25rem + env(safe-area-inset-bottom, 0px))' }"
        >
          <div class="flex gap-3.5">
            <span
              class="flex size-10 shrink-0 items-center justify-center rounded-full"
              :class="TONE_CIRCLES[props.tone]"
              aria-hidden="true"
            >
              <AppIcon
                :name="TONE_ICONS[props.tone]"
                :size="20"
              />
            </span>

            <div class="min-w-0 flex-1">
              <h2
                :id="titleId"
                class="text-[15px] font-semibold leading-snug"
                v-text="props.title"
              />
              <p
                :id="messageId"
                class="mt-1.5 text-sm leading-relaxed text-slate-300"
                v-text="props.message"
              />

              <ul
                v-if="props.details.length > 0"
                class="mt-3 space-y-1.5 rounded-xl border border-line bg-ink-850 px-3.5 py-3"
              >
                <li
                  v-for="(item, index) in props.details"
                  :key="index"
                  class="flex gap-2 text-xs leading-relaxed text-slate-400"
                >
                  <span
                    class="mt-1.5 size-1.5 shrink-0 rounded-full bg-slate-500"
                    aria-hidden="true"
                  />
                  <span v-text="item" />
                </li>
              </ul>
            </div>
          </div>
        </div>

        <footer
          class="flex shrink-0 flex-col-reverse gap-2 border-t border-line px-4 py-3 sm:flex-row sm:justify-end sm:px-6 sm:py-4"
          :style="{ paddingBottom: 'calc(0.75rem + env(safe-area-inset-bottom, 0px))' }"
        >
          <BaseButton
            variant="secondary"
            :class="cancelClass"
            :disabled="props.pending"
            @click="emit('cancel')"
          >
            {{ props.cancelLabel }}
          </BaseButton>
          <BaseButton
            :variant="TONE_BUTTONS[props.tone]"
            :class="confirmClass"
            :loading="props.pending"
            @click="emit('confirm')"
          >
            {{ props.confirmLabel }}
          </BaseButton>
        </footer>
      </div>
    </div>
  </Teleport>
</template>
