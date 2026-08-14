<script setup lang="ts">
import { ref, useId } from 'vue'

import { useModalHost } from '@/shared/lib/useModalHost'

import AppIcon from './AppIcon.vue'

/**
 * PASTDAN CHIQUVCHI YARIM VARAQ (half-height bottom sheet).
 *
 * Talab: *"leaderboardda natijasi ustiga bossa background transparent
 * bo'lishi kerak, butun oynani emas yarmicha oynani pastdan qoplashi kerak;
 * telefon holatida pastdan tepaga chiqadigan modal"*.
 *
 * ══════════════════════════════════════════════════════════════════════
 *  NEGA YANGI KOMPONENT, NEGA `BaseModal` GA TO'RTINCHI REJIM EMAS
 * ══════════════════════════════════════════════════════════════════════
 *
 * `BaseModal` allaqachon UCH rejimli (markaz / keng / sheet) va uning
 * shablonidagi har bir klass ikki-uch shartli ifodadan o'tadi. To'rtinchi
 * rejim qo'shish 30+ CHAQIRUV JOYINING hammasini xavf ostiga qo'yardi:
 * bitta noto'g'ri shart shoxi xodim oynalarini ham o'zgartirib yuborardi.
 *
 * Loyihada bu qaror BIR MARTA qabul qilingan va yozib qo'yilgan:
 * `BaseDrawer.vue:14-17` — *"NEGA `BaseModal` ga to'rtinchi rejim EMAS: u
 * allaqachon uch rejimli va yana bir shart shoxi uni o'qib bo'lmas
 * qilardi"*. Shu naqsh takrorlanadi: KO'RINISH ajratiladi, MEXANIZM esa
 * ajratilmaydi.
 *
 * 🔴 MEXANIZM `useModalHost` DAN (skroll qulfi, ESC steki, fokus tuzog'i,
 *    fokusni qaytarish). `useModalHost.ts:32-37` buni TALAB qiladi: qo'lda
 *    yozilgan nusxa aynan uchta xatoni qaytaradi (sanoqsiz qulf, qo'shni
 *    qatlamni yopadigan ESC, fokus tuzog'ining yo'qligi).
 *
 * ★ `kind: 'dialog'`, `'drawer'` EMAS: `drawer` qatlami ikkinchi drawer
 *   ustida ochilsa dev'da ogohlantirish chiqadi (`useModalHost.ts:232-239`),
 *   varaq esa panel ustida ochilishi MUMKIN — u ekranning yarmini egallaydi,
 *   "qaysi panel orqada" degan savol tug'ilmaydi.
 *
 * ★ BALANDLIK `max-h` + `min-h`, QAT'IY `h-[50dvh]` EMAS: Android'da
 *   klaviatura ochilganda `dvh` KICHRAYADI va qat'iy balandlikdagi varaq
 *   kontentni qirqib qo'yardi. Diapazon esa qisqa kontentda ham "yarim
 *   ekran" ko'rinishini saqlaydi (`min-h`), uzun kontentda esa ichki
 *   skrollga o'tadi (`max-h`).
 *
 * ★ DESKTOPDA HAM PASTDAN: `BaseModal` `sheet` rejimi bilan bir xil qaror —
 *   o'quvchi ilovasi (Telegram Mini App) "pastdan chiqadigan varaq"
 *   ko'rinishida, kenglik esa 520px bilan cheklanadi.
 */
const props = defineProps<{
  open: boolean
  /** Sarlavha — `aria-labelledby` ham shundan oladi, bo'sh qoldirilmaydi. */
  title: string
}>()

const emit = defineEmits<{ close: [] }>()

const panel = ref<HTMLElement | null>(null)
const titleId = useId()

useModalHost({
  open: () => props.open,
  onClose: () => emit('close'),
  panel,
  kind: 'dialog',
})
</script>

<template>
  <Teleport to="body">
    <!--
      ══════════════════════════════════════════════════════════════════
       FON: KO'RINMAS, LEKIN BOSILADIGAN
      ══════════════════════════════════════════════════════════════════

      Talab "background transparent bo'lishi kerak" deydi — ya'ni ostidagi
      reyting ro'yxati TO'LIQ ko'rinib turadi (na tus, na blur). Shuning
      uchun bu yerda `BaseModal` dagi `bg-slate-900/35 backdrop-blur-sm`
      YO'Q.

      🔴 `pointer-events-none` QO'SHILMAYDI. Fon — YOPISH NISHONI:
         `@click.self` aynan shu elementga bosilganda ishlaydi
         (`BaseModal.vue:105` bilan bir xil naqsh). `pointer-events: none`
         bo'lsa bosish ostidagi sahifaga o'tib ketardi va varaq faqat
         X tugmasi yoki ESC bilan yopilardi — telefonda esa foydalanuvchi
         AVVAL yon tomonga bosadi.

      ★ SHUNING UCHUN OSTIDAGI SAHIFA KO'RINADI, LEKIN BOSILMAYDI. Bu
        ONGLI: varaq modal qatlam (`aria-modal="true"`), fokus uning ichida
        qulflangan va skroll ham qulflangan. "Ko'rinadi = ishlaydi" degan
        taassurotni PANEL O'ZI to'g'irlaydi: birinchi bosish uni yopadi,
        ya'ni foydalanuvchi darhol ro'yxatga qaytadi.
    -->
    <div
      v-if="props.open"
      class="fixed inset-0 z-50 flex items-end justify-center"
      role="presentation"
      @click.self="emit('close')"
    >
      <!--
        Panel radiusi 1.25rem (20px) — `BaseModal` bilan AYNI qiymat, varaq
        boshqa komponentdan chiqqani bilinmasin.

        ★ CHEGARA `line-strong`, `line` EMAS: ko'rinmas fonda panelni
          sahifadan ajratib turadigan YAGONA vosita — chegara va soya.
          Yorug' temada `--color-line` (#eceff5) oq panelning oq sahifa
          ustidagi qirrasini deyarli ko'rsatmasdi.
      -->
      <div
        ref="panel"
        class="flex max-h-[55dvh] min-h-[40dvh] w-full max-w-[520px] animate-sheet-up flex-col overflow-hidden rounded-t-[1.25rem] border-x border-t border-line-strong bg-ink-900 shadow-lg"
        role="dialog"
        aria-modal="true"
        :aria-labelledby="titleId"
        tabindex="-1"
      >
        <header
          class="flex shrink-0 items-center gap-3 border-b border-line px-4 py-3 sm:px-6 sm:py-4"
        >
          <h2
            :id="titleId"
            class="min-w-0 flex-1 truncate text-[15px] font-semibold"
            v-text="props.title"
          />
          <!-- `aria-label` — tugma ichida faqat ikonka bor (`BaseModal` bilan bir xil). -->
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
          ★ `overscroll-contain` (`BaseDrawer` dagidek): varaq oxirigacha
            skroll qilinganda harakat ORQADAGI sahifaga o'tib ketmasin.
            Ko'rinmas fonda bu xato ayniqsa yomon ko'rinardi — foydalanuvchi
            varaqni surayotganda ostidagi reyting ro'yxati siljib ketardi.

          ★ Pastki padding safe-area'ni hisobga oladi (`BaseModal` `sheet`
            rejimidan KO'CHIRILDI, `:160-165`): iPhone'dagi "home indicator"
            varaqning oxirgi qatorini yopib qo'ymasin.
        -->
        <div
          class="scrollbar-slim min-h-0 flex-1 overflow-y-auto overscroll-contain px-4 py-4 sm:px-6 sm:py-5"
          :style="{ paddingBottom: 'calc(1.5rem + env(safe-area-inset-bottom, 0px))' }"
        >
          <slot />
        </div>
      </div>
    </div>
  </Teleport>
</template>
