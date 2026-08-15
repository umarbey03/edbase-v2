<script setup lang="ts">
import { computed } from 'vue'

import { useAvatar } from '@/entities/user'
import type { User } from '@/entities/user'
import { formatPhone } from '@/shared/lib/phone'
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

const emit = defineEmits<{ close: []; logout: []; edit: [] }>()

const fullName = computed(() => props.user?.fullName ?? '')
const initial = computed(() => (fullName.value.trim()[0] ?? '?').toUpperCase())

/**
 * Profil rasmi — `blob:` manzil (sabab `useAvatar` izohida).
 * `null` bo'lsa ism harfi chiziladi.
 */
const avatarUrl = useAvatar(
  computed(() => props.user?.id ?? null),
  computed(() => props.user?.avatarUpdatedAt ?? null),
)

const phone = computed(() => {
  const value = formatPhone(props.user?.phone)
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
      <!--
        Yopish tugmasi — DOIRA ichida, yengil sirt bilan.

        ★ Ilgari u sirtsiz «×» edi va oq panelda "osilib" turardi: keng
        ekranda modal markazga chiqqach, uning yagona ikonkasi eng
        yuqori o'ng burchakda hech narsaga bog'lanmagan belgi bo'lib
        ko'rinardi. Doira uni panelning bir qismiga aylantiradi va
        `BaseModal` ning O'Z sarlavha tugmasi bilan bir uslubda bo'ladi.
      -->
      <button
        type="button"
        class="tap-expand absolute -right-1 -top-1 flex size-9 items-center justify-center rounded-full text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
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
      <img
        v-if="avatarUrl !== null"
        :src="avatarUrl"
        class="mb-3.5 mt-2.5 size-20 rounded-full object-cover shadow-md"
        alt=""
      >
      <div
        v-else
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
      <p class="mb-4 mt-2">
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

      <!--
        ★ IKKALA BLOK HAM `w-full`, ya'ni telefon raqami, iqtibos va
        "Yopish" tugmasi BIR XIL kenglikda turadi.

        Ilgari raqam `inline-block min-w-[170px]` edi — telefonda bu
        sezilmasdi (blok baribir ekran kengligining yarmi edi), lekin
        markazlashgan oynada u qolgan ikkitasidan tor bo'lib chiqib,
        ustunning o'ng chekkasini "yirtib" turardi. Bir ustunda uch xil
        kenglik — tartibsizlikning eng ko'zga tashlanadigan turi.
      -->
      <div class="mb-3 w-full">
        <p class="mb-1.5 text-[11px] uppercase tracking-[0.8px] text-slate-400">
          Telefon raqam
        </p>
        <p
          class="w-full rounded-xl border border-line bg-ink-800 px-4 py-2.5 text-[15px] font-bold tracking-[0.5px] text-slate-100"
          v-text="phone"
        />
      </div>

      <p
        class="mb-5 w-full rounded-xl border border-line bg-ink-800 px-4 py-3 text-[13px] italic leading-relaxed text-slate-400"
      >
        “Muvaffaqiyatga erishish uchun doimiy o‘rganish va tinimsiz harakat qilish lozim.”
      </p>

      <!--
        ★ "TAHRIRLASH" — BIRINCHI va ASOSIY (`primary`) tugma, "Yopish"
        esa ikkinchi darajali. Ilgari bu yerda faqat "Yopish" turardi,
        ya'ni oyna hech qanday AMAL taklif qilmasdi — u faqat ma'lumot
        ko'rsatardi. Loyiha egasi aynan shu bo'shliqni to'ldirishni
        so'radi.
      -->
      <BaseButton
        variant="primary"
        size="lg"
        block
        @click="emit('edit')"
      >
        <span class="inline-flex items-center gap-2">
          <AppIcon
            name="edit"
            :size="16"
          />
          Tahrirlash
        </span>
      </BaseButton>

      <BaseButton
        class="mt-2.5"
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
