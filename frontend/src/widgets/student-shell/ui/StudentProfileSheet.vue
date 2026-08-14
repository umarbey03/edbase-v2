<script setup lang="ts">
import { computed } from 'vue'

import type { User } from '@/entities/user'
import { AppIcon, BaseButton, BaseModal } from '@/shared/ui'

/**
 * Profil varag'i (eski `#profile-modal`).
 *
 * ★ TELEFON RAQAM: 2026-08-14 dan `GET /api/v1/auth/me` (`UserDto`) uni
 * QAYTARADI — maydon talab R8 (video suv belgisi) uchun qo'shilgan va u
 * O'Z-O'ZIGA CHEKLANGAN (javob tokendagi `sub` dan chiqadi), ya'ni bu yerda
 * ishlatilishi hech qanday begona ma'lumot ochmaydi.
 *
 * ⚠️ ILGARI shu yerda qotib qolgan "Kiritilmagan" matni turardi (maydon
 * mavjud emas edi). Endi u FAQAT raqam haqiqatan yo'q bo'lganda chiqadi —
 * bunday o'quvchilar bor va ular Telegram'ni ham ulay olmaydi.
 * O'ylab topilgan raqam CHIZILMAYDI.
 */
const props = defineProps<{
  open: boolean
  user: User | null
}>()

const emit = defineEmits<{ close: []; logout: [] }>()

const fullName = computed(() => props.user?.fullName ?? '')
const initial = computed(() => (fullName.value.trim()[0] ?? '?').toUpperCase())

const phone = computed(() => {
  const value = props.user?.phone?.trim() ?? ''
  return value.length > 0 ? value : 'Kiritilmagan'
})
</script>

<template>
  <BaseModal
    :open="props.open"
    title=""
    sheet
    @close="emit('close')"
  >
    <div class="relative flex flex-col items-center px-1 text-center">
      <button
        type="button"
        class="tap-target absolute -top-1 right-0 flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:text-slate-100"
        aria-label="Yopish"
        @click="emit('close')"
      >
        <AppIcon
          name="close"
          :size="18"
        />
      </button>

      <!--
        Avatar: gradient TOKENLARDA (ilgari `#f5b731 -> #22d3ee` va oltin
        "nur" soyasi QOTIB QOLGAN edi). Yorug' temada nur soyasi o'rniga
        oddiy `shadow-md`: oq fonda rangli glow iflos halqa bo'lib ko'rinadi.
        `StudentAppBar` dagi kichik avatar bilan AYNAN bir xil gradient —
        ikkisi bitta odamni ko'rsatadi.
      -->
      <div
        class="mb-3.5 mt-2.5 flex size-20 items-center justify-center rounded-full bg-gradient-to-br from-brand-500 to-violet-400 text-[32px] font-bold text-white shadow-md"
        aria-hidden="true"
      >
        {{ initial }}
      </div>

      <h2
        class="text-[19px] font-extrabold text-slate-100"
        v-text="fullName"
      />

      <!--
        Holat nishoni — pastel tint + to'q matn. Nuqtadagi
        `box-shadow: 0 0 8px` glow OLIB TASHLANGAN: yorug' fonda u nuqtani
        xira dog'ga aylantiradi. (Undan oldin `#22d3ee` matn + 12% tint
        qotib qolgan edi va oq fonda 1.6:1 berib o'qilmasdi.)

        ══════════════════════════════════════════════════════════════════
        R19 — NEGA CYAN EMAS, BREND OILASI (qaror, 2026-08-13)
        ══════════════════════════════════════════════════════════════════

        Bu yozuv — NISHON matni, logotip emas: u sarlavha o'rnida turmaydi,
        ichida odatiy so'zlar bor ("Online o'quvchisi") va yonida holat
        nuqtasi bor. SHUNGA QARAMAY uning ICHIDA brend nomi bor va u
        ilovadagi UCHINCHI rang oilasida (`cyan`) chizilardi — ya'ni bitta
        ekranda "Zin-Nur" indigo, "ZIN-NUR" esa ko'k bo'lib chiqardi.

        IKKI YO'LDAN BIRI TANLANDI:
          (a) nishon ichida faqat "ZIN-NUR" ni aksentga bo'yash — RAD
              ETILDI: u aynan R19 to'g'irlayotgan narsani, bitta qatorda
              ikki rangni, QAYTA yaratardi;
          (b) butun nishonni brend oilasiga o'tkazish — TANLANDI: nishon
              bir rangli bo'lib qoladi, brend nomi esa hamma joydagi
              rangda chiziladi.

        ★ TOKENLAR TEKSHIRILGAN JUFTLIKDAN: `text-brand-300` +
        `bg-brand-500/12` — `BaseBadge` ning `accent` toni bilan AYNI va
        `scripts/contrast-audit.mjs` da alohida qator sifatida bor
        ("nishon: text-brand-300 / brand tint 12%", 4.5:1). Ya'ni yangi
        qiymat kiritilmadi va auditni qayta yurgizish shart emas.

        ★ MATN O'ZGARMADI — "ZIN-NUR Online o'quvchisi" aynan shundayligicha.
      -->
      <p class="mb-5 mt-2">
        <span
          class="inline-flex items-center gap-1.5 rounded-full border border-brand-500/25 bg-brand-500/12 px-3 py-[5px] text-xs font-semibold text-brand-300"
        >
          <span
            class="size-2 rounded-full bg-brand-500"
            aria-hidden="true"
          />
          ZIN-NUR Online o‘quvchisi
        </span>
      </p>

      <div class="mb-4 w-full">
        <p class="mb-1.5 text-[11px] uppercase tracking-[0.8px] text-slate-400">
          Telefon raqam
        </p>
        <p
          class="inline-block min-w-[170px] rounded-xl border border-line bg-ink-800 px-4 py-2.5 text-[15px] font-bold tracking-[0.5px] text-slate-100"
          v-text="phone"
        />
      </div>

      <p
        class="mb-6 w-full rounded-xl border border-line bg-ink-800 px-4 py-3 text-[13px] italic leading-relaxed text-slate-400"
      >
        “Muvaffaqiyatga erishish uchun doimiy o‘rganish va tinimsiz harakat qilish lozim.”
      </p>

      <BaseButton
        variant="secondary"
        size="lg"
        block
        @click="emit('close')"
      >
        Yopish
      </BaseButton>

      <!--
        "Chiqish" eski ilovada YO'Q EDI: u Telegram Mini App bo'lgani uchun
        sessiyani Telegram boshqarardi. v2 oddiy brauzerda ochiladi, ya'ni
        chiqish yo'li bo'lmasa o'quvchi umumiy kompyuterda tizimda qolib
        ketardi. Shuning uchun ATAYLAB qo'shildi, lekin ikkinchi darajali
        ko'rinishda — asosiy amal baribir "Yopish".
      -->
      <button
        type="button"
        class="tap-target mt-3 inline-flex items-center justify-center gap-2 text-[13px] font-semibold text-slate-400 transition-colors hover:text-rose-400"
        @click="emit('logout')"
      >
        <AppIcon
          name="logout"
          :size="15"
        />
        Chiqish
      </button>
    </div>
  </BaseModal>
</template>
