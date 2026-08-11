<script setup lang="ts">
import { ref, useId } from 'vue'

import { useModalHost } from '@/shared/lib/useModalHost'

import AppIcon from './AppIcon.vue'

/**
 * Modal oyna.
 *
 * TELEFONDA to'liq ekran (pastdan chiquvchi sheet), DESKTOPDA markazlashgan
 * oyna — 390px ekranda markazlashgan "kichik" oyna forma maydonlarini
 * siqib qo'yadi va klaviatura ochilganda kontent ko'rinmay qoladi.
 *
 * ══════════════════════════════════════════════════════════════════════
 *  🔴 ICHKI MEXANIKA `useModalHost` GA O'TKAZILDI (2026-08-11)
 * ══════════════════════════════════════════════════════════════════════
 *
 * TASHQI KO'RINISH VA API O'ZGARMADI (`open`, `title`, `wide`, `sheet`,
 * `close`, `default`/`footer` slotlari) — komponent 30+ joyda ishlatilgan,
 * bu FAQAT ichki refaktor.
 *
 * Ilgari bu fayl skroll qulfi, ESC va fokusni O'ZI boshqarardi va shu
 * nusxada UCHTA yashirin xato bor edi:
 *
 *  1. SKROLL QULFI SANOQSIZ. `unlock()` `body.style.overflow` ni saqlangan
 *     qiymatga tiklardi. Modal USTIDA yana bir oyna ochilib yopilsa
 *     (masalan `ConfirmDialog`), ikkinchisi qulfni ochib yuborardi va
 *     ostidagi sahifa hali ochiq modal ortida skrollga tushardi.
 *     `useModalHost` da qulf SANOQLI: faqat OXIRGI qatlam yopilganda
 *     ochiladi.
 *
 *  2. ESC STEKI YO'Q. Har oyna `document` ga o'z `keydown` ishlovchisini
 *     qo'yardi; `stopPropagation` esa BIR XIL elementdagi boshqa
 *     ishlovchini to'xtatmaydi. Ya'ni drawer ustida modal ochilsa ESC
 *     IKKI qatlamni birga yopardi — foydalanuvchi kichik oynani bekor
 *     qilmoqchi bo'lib butun panelni yo'qotardi. Endi bitta umumiy
 *     ishlovchi bor va u faqat stek TEPASIDAGI qatlamga tegadi.
 *
 *  3. FOKUS PANELGA BERILARDI, birinchi maydonga emas: forma ochilganda
 *     foydalanuvchi yozishni boshlash uchun qo'shimcha Tab bosishi kerak
 *     edi. Endi `MODAL_AUTOFOCUS_CLASS` (`js-modal-autofocus`) klassi
 *     qo'yilgan element fokus oladi; qo'yilmagan bo'lsa xatti-harakat
 *     AVVALGIDEK qoladi (panelning o'zi) — ya'ni 30+ chaqiruv joyining
 *     birortasi buzilmaydi. Fokus TUZOG'I (Tab halqasi) esa endi
 *     hammasida ishlaydi, ilgari umuman yo'q edi.
 *
 * ★ `data-*` ATRIBUT ISHLATILMAYDI, KLASS ishlatiladi: `strictTemplates`
 * yoqilgan va komponentga e'lon qilinmagan atribut berish tur xatosi
 * beradi (`DAVOM_ETTIRISH.md` tuzoqlari, 19-band).
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
const titleId = useId()

/*
  Skroll qulfi (sanoqli), fokusni qaytarish, fokus tuzog'i va ESC steki —
  hammasi shu composable'da. Yuqoridagi izohda nima uchun ekani yozilgan.

  ★ `kind: 'dialog'` — `drawer` EMAS: modal drawer ustida ochilishi RUXSAT
  etilgan (aynan shu uchun qatlam steki kerak edi), ichma-ich drawer esa
  taqiqlangan va `useModalHost` uni faqat `kind: 'drawer'` da ushlaydi.
*/
useModalHost({
  open: () => props.open,
  onClose: () => emit('close'),
  panel,
  kind: 'dialog',
})
</script>

<template>
  <Teleport to="body">
    <div
      v-if="props.open"
      class="fixed inset-0 z-50 flex bg-slate-900/35 backdrop-blur-sm"
      :class="
        props.sheet
          ? 'items-end justify-center'
          : 'sm:items-center sm:justify-center sm:p-5'
      "
      role="presentation"
      @click.self="emit('close')"
    >
      <!--
        Panel radiusi 1.25rem (20px) — Tailwind'da tayyor qadam yo'q
        (`rounded-2xl` 16px, `rounded-3xl` 24px), shuning uchun aniq qiymat.
        `shadow-lg` chegara o'rniga IYERARXIYA beradi: yorug' temada
        1px `line` chegara oq panelda deyarli ko'rinmaydi va oyna sahifaga
        "yopishib" qolardi.
      -->
      <div
        ref="panel"
        class="flex max-h-dvh w-full animate-sheet-up flex-col overflow-hidden border-line bg-ink-900 shadow-lg"
        :class="
          props.sheet
            ? 'max-w-[520px] rounded-t-[1.25rem] border-x border-t'
            : [
              'sm:max-h-[92dvh] sm:animate-fade-up sm:rounded-[1.25rem] sm:border',
              props.wide ? 'sm:max-w-3xl' : 'sm:max-w-lg',
            ]
        "
        role="dialog"
        aria-modal="true"
        :aria-labelledby="props.title.length > 0 ? titleId : undefined"
        tabindex="-1"
      >
        <header
          v-if="props.title.length > 0"
          class="flex shrink-0 items-center gap-3 border-b border-line px-4 py-3 sm:px-6 sm:py-4"
        >
          <h2
            :id="titleId"
            class="min-w-0 flex-1 truncate text-[15px] font-semibold"
            v-text="props.title"
          />
          <!-- `aria-label` QO'SHILDI: `title` atributi ekran o'qigichlar uchun
               ishonchli nom emas (brauzerga qarab o'qilmasligi mumkin), tugma
               ichida esa faqat ikonka bor. `BaseDrawer` da allaqachon shunday. -->
          <button
            type="button"
            class="tap-target -mr-2 flex items-center justify-center rounded-xl text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
            title="Yopish"
            aria-label="Yopish"
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
