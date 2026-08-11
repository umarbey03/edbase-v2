<script setup lang="ts">
import { ref, useId } from 'vue'

import { useModalHost } from '@/shared/lib/useModalHost'

import AppIcon from './AppIcon.vue'

/**
 * O'NGDAN CHIQUVCHI PANEL (drawer) — ekranning 85% ini egallaydi.
 *
 * Talab: *"modal ko'rinishida ekranni o'ng tarafidan ekranni 85% egallab
 * chiqishi kerak"* — o'quvchi profili va dars tahrirlash uchun.
 *
 * NEGA `BaseModal` ga to'rtinchi rejim EMAS: u allaqachon uch rejimli
 * (markaz / keng / sheet) va yana bir shart shoxi uni o'qib bo'lmas qilardi.
 * Lekin MEXANIZM takrorlanmaydi: skroll qulfi, fokus qaytarish, fokus tuzog'i
 * va ESC `shared/lib/useModalHost.ts` da — bitta joyda tuzatiladi.
 *
 * O'LCHAMLAR (talabdagi taqsimot):
 *   telefon  (<640px) : TO'LIQ EKRAN — 390px da 85% ma'nosiz, qolgan 15%
 *                       kontentni siqadi va orqa sahifadan foyda yo'q;
 *   planshet (>=640px): 92vw;
 *   desktop  (>=1024px): 85vw, lekin `max-width: 1240px` — 27" monitorda
 *                       85% = 2000px+ bo'lib, forma maydonlari cho'zilib
 *                       o'qilmas holga tushardi.
 *
 * 🔴 ICHMA-ICH DRAWER TAQIQLANGAN — `useModalHost` dev'da `console.warn`
 * bilan ushlaydi. Ichki oqim uchun `ConfirmDialog` / `BaseModal` ishlatiladi.
 */
const props = withDefaults(
  defineProps<{
    open: boolean
    /** Sarlavha — `aria-labelledby` ham shundan oladi, bo'sh qoldirilmaydi. */
    title: string
    /** Sarlavha ostidagi ikkinchi qator (masalan o'quvchi guruhi). */
    subtitle?: string
    /**
     * `true` bo'lsa ESC va fon bosilishi panelni YOPMAYDI — saqlanmagan
     * forma tasodifan yo'q bo'lmasin. Yopish faqat sarlavhadagi tugma yoki
     * chaqiruvchi mantiq orqali (u yerda `useConfirm` bilan so'raladi).
     */
    persistent?: boolean
  }>(),
  { subtitle: '', persistent: false },
)

const emit = defineEmits<{ close: [] }>()

const panel = ref<HTMLElement | null>(null)
const titleId = useId()

useModalHost({
  open: () => props.open,
  onClose: () => emit('close'),
  panel,
  kind: 'drawer',
  closeOnEscape: !props.persistent,
})

function onBackdrop(): void {
  if (props.persistent) return
  emit('close')
}
</script>

<template>
  <Teleport to="body">
    <!--
      ══════════════════════════════════════════════════════════════════
       YOPILISH O'TISHI (2026-08-11)
      ══════════════════════════════════════════════════════════════════

      `style.css` da `--animate-drawer-out` allaqachon e'lon qilingan edi,
      lekin ISHLATILMAY turgan: panel `v-if` bilan DARHOL yo'q bo'lardi —
      ochilishi silliq, yopilishi esa "chertib o'chirilgandek" edi.

      🔴 NEGA `<Transition>`, nega qo'lda `setTimeout` EMAS: fokusni
      qaytarish va skroll qulfini `useModalHost` UNMOUNT'da bajaradi
      (`onScopeDispose` + `open` kuzatuvchisi). Qo'lda kechiktirsak
      (`isClosing` flagi bilan) qulf o'tish TUGAMASDAN ochilib, panel
      surilib ketayotganda ostidagi sahifa skrollga tushardi.
      `<Transition>` esa `v-if` ni o'tish tugagach bajaradi, ya'ni
      `useModalHost` ning mantig'iga UMUMAN TEGILMAYDI: u `props.open`
      ni kuzatadi (DOM emas) va qulf `open === false` bo'lgan zahoti
      ochiladi. O'tish davomidagi 180 ms esa `pointer-events: none` bilan
      xavfsiz (pastdagi `<style>` izohi).

      ★ `appear` YO'Q: kirish animatsiyasi `animate-drawer-in` klassida
      qoladi (o'zgarmadi), `<Transition>` faqat CHIQISHNI boshqaradi.
      Shuning uchun `enter-*` klasslari bo'sh — Vue kirishda hech narsa
      qo'shmaydi va mavjud ko'rinish saqlanadi.

      ★ `prefers-reduced-motion` — `<style scoped>` da (pastda): o'tish
      0 ms ga tushadi va panel darhol yo'qoladi, ya'ni AVVALGI xatti-harakat.
    -->
    <Transition
      leave-active-class="zn-drawer-leave"
      leave-to-class="zn-drawer-leave-to"
    >
      <!--
        Fon: `bg-slate-900/35` + engil blur. Qotib qolgan `bg-black/65` EMAS —
        yorug' temada 65% qora juda og'ir ko'rinadi (reja A3).
      -->
      <div
        v-if="props.open"
        class="fixed inset-0 z-50 flex justify-end bg-slate-900/35 backdrop-blur-sm"
        role="presentation"
        @click.self="onBackdrop"
      >
        <!--
          Sarlavha va futer `shrink-0`, tana esa yakka skroll qiluvchi blok —
          ya'ni sarlavha/futer YOPISHIB turadi (`position: sticky` hiylasisiz,
          shuning uchun ular ostidan kontent "sizib" chiqmaydi).
        -->
        <div
          ref="panel"
          class="flex h-dvh w-full animate-drawer-in flex-col overflow-hidden bg-ink-900 shadow-lg sm:w-[92vw] lg:w-[85vw] lg:max-w-[1240px] sm:border-l sm:border-line"
          role="dialog"
          aria-modal="true"
          :aria-labelledby="titleId"
          tabindex="-1"
        >
          <header
            class="flex shrink-0 items-center gap-3 border-b border-line px-4 py-3 sm:px-6 sm:py-4"
          >
            <div class="min-w-0 flex-1">
              <h2
                :id="titleId"
                class="truncate text-[15px] font-semibold"
                v-text="props.title"
              />
              <p
                v-if="props.subtitle.length > 0"
                class="mt-0.5 truncate text-xs text-slate-400"
                v-text="props.subtitle"
              />
            </div>

            <!-- Panel darajasidagi amallar (saqlash, "yangilash", menyu). -->
            <div
              v-if="$slots.actions"
              class="flex shrink-0 items-center gap-2"
            >
              <slot name="actions" />
            </div>

            <button
              type="button"
              class="tap-target -mr-2 flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
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

          <div
            class="scrollbar-slim min-h-0 flex-1 overflow-y-auto overscroll-contain px-4 py-4 sm:px-6 sm:py-5"
            :style="
              $slots.footer
                ? {}
                : { paddingBottom: 'calc(1.25rem + env(safe-area-inset-bottom, 0px))' }
            "
          >
            <slot />
          </div>

          <!--
            Futer pastki paddingi safe-area'ni hisobga oladi: iPhone'dagi "home
            indicator" ustidagi tugmani yopib qo'ymasin.
          -->
          <footer
            v-if="$slots.footer"
            class="flex shrink-0 flex-col-reverse gap-2 border-t border-line px-4 py-3 sm:flex-row sm:justify-end sm:px-6 sm:py-4"
            :style="{ paddingBottom: 'calc(0.75rem + env(safe-area-inset-bottom, 0px))' }"
          >
            <slot name="footer" />
          </footer>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<!--
  ★ NEGA `<style scoped>`, Tailwind utility EMAS:

  Chiqish o'tishi IKKI elementga tegishi kerak — qoraytiruvchi qatlam
  (`opacity`) va panel (`transform`). Panel `v-if` li elementning ICHIDA
  turadi, Vue esa o'tish klasslarini FAQAT ildiz elementga qo'yadi. Bolaga
  tegish uchun selektor kerak, selektor esa utility'da yo'q.

  `scoped` — `data-v-*` atributi orqali ishlaydi, ya'ni uslub global
  bo'lib sizib chiqmaydi (Teleport ichida ham shu komponent daraxti).
-->
<style scoped>
/*
  `--animate-drawer-out` `style.css` da e'lon qilingan (0.18s ease-in,
  `translateX(0)` → `translateX(100%)`) va SHU YERGACHA ISHLATILMAY turgan.

  ★ Qatlamning O'ZI faqat xiralashadi, surilmaydi: u butun ekranni egallaydi
  va uni surish o'ng chetda oq chiziq bo'lib ko'rinardi.
*/
.zn-drawer-leave {
  transition: opacity 0.18s ease-in;
  /*
    🔴 O'tish davomida qatlam BOSILMAYDI. Ikki sabab:
     • `useModalHost` skroll qulfini `open === false` bo'lgan zahoti ochadi
       (u DOM'ni emas, prop'ni kuzatadi) — ya'ni 180 ms davomida panel hali
       ko'rinadi, ostidagi sahifa esa allaqachon "tirik". Bosish o'tib
       ketmasin;
     • yopish tugmasi ikki marta bosilishi (`close` ikki marta emit
       qilinishi) ham shu bilan to'siladi.
  */
  pointer-events: none;
}

.zn-drawer-leave > [role='dialog'] {
  animation: var(--animate-drawer-out);
}

.zn-drawer-leave-to {
  opacity: 0;
}

/*
  🔴 HARAKATNI KAMAYTIRISH. `prefers-reduced-motion: reduce` da o'tish
  ko'rinmaydi — panel darhol yo'qoladi (refaktordan OLDINGI xatti-harakat).

  `animation: none` YETMAYDI: Vue `<Transition>` `transitionend` /
  `animationend` hodisasini kutadi va hodisa kelmasa element DOM'da
  MUZLAB qolardi (drawer yopilmaydi — jimgina xato). Shuning uchun
  davomiylik 0.01ms ga tushiriladi: hodisa keladi, ko'z harakatni sezmaydi.
*/
@media (prefers-reduced-motion: reduce) {
  .zn-drawer-leave,
  .zn-drawer-leave > [role='dialog'] {
    transition-duration: 0.01ms;
    animation-duration: 0.01ms;
  }
}
</style>
