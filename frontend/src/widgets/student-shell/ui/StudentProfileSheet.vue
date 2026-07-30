<script setup lang="ts">
import { computed } from 'vue'

import type { User } from '@/entities/user'
import { AppIcon, BaseButton, BaseModal } from '@/shared/ui'

/**
 * Profil varag'i (eski `#profile-modal`).
 *
 * ★ TELEFON RAQAM: `GET /api/v1/auth/me` (`UserDto`) da telefon maydoni YO'Q —
 * u faqat `UserDetailsDto` da bor, u esa `/api/v1/users` orqali va faqat
 * o'quv bo'limi/adminga ochiq (o'quvchi 403 oladi). Shuning uchun raqam
 * o'rniga eski ilovaning O'Z zaxira matni ko'rsatiladi: `ME_PHONE ||
 * 'Kiritilmagan'`. O'ylab topilgan raqam CHIZILMAYDI.
 */
const props = defineProps<{
  open: boolean
  user: User | null
}>()

const emit = defineEmits<{ close: []; logout: [] }>()

const fullName = computed(() => props.user?.fullName ?? '')
const initial = computed(() => (fullName.value.trim()[0] ?? '?').toUpperCase())
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

      <div
        class="mb-3.5 mt-2.5 flex size-20 items-center justify-center rounded-full text-[32px] font-bold text-white"
        style="
          background: linear-gradient(135deg, #f5b731, #22d3ee);
          box-shadow: 0 0 15px rgb(245 183 49 / 0.25);
        "
        aria-hidden="true"
      >
        {{ initial }}
      </div>

      <h2
        class="text-[19px] font-extrabold text-slate-100"
        v-text="fullName"
      />

      <p class="mb-5 mt-2">
        <span
          class="inline-flex items-center gap-1.5 rounded-full border px-3 py-[5px] text-xs font-semibold"
          style="
            background: rgb(34 211 238 / 0.12);
            border-color: rgb(34 211 238 / 0.25);
            color: #22d3ee;
          "
        >
          <span
            class="size-2 rounded-full"
            style="background: #22d3ee; box-shadow: 0 0 8px #22d3ee"
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
        >
          Kiritilmagan
        </p>
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
