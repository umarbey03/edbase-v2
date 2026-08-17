<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, onScopeDispose, ref, watch } from 'vue'

import { dropAvatar, removeAvatar, uploadAvatar, useAvatar } from '@/entities/user'
import type { User } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { toUserMessage } from '@/shared/api'
import { useConfirm } from '@/shared/lib/useConfirm'
import { showToast } from '@/shared/lib/useToast'
import { AppIcon, BaseButton, BaseModal, BaseSpinner } from '@/shared/ui'

/**
 * ============================================================================
 *  PROFILNI TAHRIRLASH — FAQAT RASM (2026-08-15, 2026-08-17 da qisqartirildi)
 * ============================================================================
 *
 * ⚠️ ISM VA TELEFONNI O'ZI TAHRIRLASH OLIB TASHLANDI (2026-08-17, loyiha
 * egasining qarori): *"foydalanuvchi o'z ism familyasi va nomerini edit
 * qilish imkoniga ega bo'lmasligi kerak"* — BARCHA rol uchun (xodim ham).
 * Bu ikkala maydonni endi FAQAT o'quv bo'limi/admin "Foydalanuvchilar"
 * panelidan (`UserFormDialog`) o'zgartira oladi.
 *
 * Shu bilan birga telefon ALMASHTIRISH oqimi ham (Telegram tasdig'i bilan)
 * butunlay olib tashlandi — sabab va tafsilot serverdagi
 * `TelegramUpdateHandler.HandleContactAsync` izohida.
 *
 * ★ RASM QOLDI: u kirish yoki huquqlarga ta'sir qilmaydigan, arzon
 * xato bilan tuzatiladigan yagona maydon — shuning uchun o'zini o'zi
 * boshqarish xavfsiz.
 */
const props = defineProps<{
  open: boolean
  user: User | null
}>()

const emit = defineEmits<{ close: [] }>()

const confirm = useConfirm()
const auth = useAuthStore()

/**
 * ⚠️ 2026-08-15 — TUZATILDI: PROFIL HAR SAQLASHDAN KEYIN DARHOL
 *    YANGILANADI (ilgari faqat oyna YOPILGANDA yangilanardi).
 *
 * Loyiha egasi: *"profil rasm yuklashda yuklanganida hech narsa
 * bilinmayapti, lekin yuklanibdi"*.
 *
 * SABAB: rasm manzili `props.user.avatarUpdatedAt` ga bog'langan
 * (`useAvatar` kaliti). Profil yangilanmasa, kalit ham o'zgarmasdi —
 * ya'ni yuklash muvaffaqiyatli tugagan bo'lsa ham ekranda ESKI rasm
 * (yoki ism harfi) qolaverardi.
 *
 * Endi har muvaffaqiyatli amaldan keyin `reloadProfile()` chaqiriladi:
 * bitta qo'shimcha `GET /auth/me` — evaziga sarlavhadagi va yon
 * menyudagi avatar ham SHU ZAHOTI yangilanadi (ular ham AYNI
 * `auth.user` dan oziqlanadi).
 */
const userId = computed(() => props.user?.id ?? null)
const avatarVersion = computed(() => props.user?.avatarUpdatedAt ?? null)
const avatarUrl = useAvatar(userId, avatarVersion)

/** Ekranda ko'rinadigan rasm — mahalliy ko'rinish USTUN. */
const shownAvatar = computed(() => preview.value ?? avatarUrl.value)

const initial = computed(() => (props.user?.fullName?.trim()[0] ?? '?').toUpperCase())

const fileInput = ref<HTMLInputElement | null>(null)
const avatarError = ref<string | null>(null)

/**
 * TANLANGAN FAYLNING MAHALLIY KO'RINISHI (`blob:` manzil).
 *
 * ★ NEGA KERAK: server javobi va undan keyingi `GET /auth/me` + rasmni
 * qayta yuklash — bu bir necha yuz millisekund. Shu vaqt ichida ekranda
 * ESKI rasm turardi va foydalanuvchi "bosdim, hech nima bo'lmadi" deb
 * ikkinchi marta fayl tanlardi.
 *
 * Mahalliy ko'rinish DARHOL chiziladi: brauzer faylni allaqachon
 * xotirasida ushlab turibdi, hech qanday tarmoq so'rovi kerak emas.
 */
const preview = ref<string | null>(null)

function clearPreview(): void {
  if (preview.value === null) return

  URL.revokeObjectURL(preview.value)
  preview.value = null
}

/**
 * MAHALLIY KO'RINISHDAN HAQIQIY RASMGA O'TISH.
 *
 * ★ Ko'rinish `onSuccess` da DARHOL o'chirilmaydi: server javob bergan
 * paytda yangi rasm HALI yuklanmagan bo'ladi (`useAvatar` uni endi
 * tortmoqchi). O'sha lahzada ko'rinish olib tashlansa, avatar bir
 * freym uchun ism harfiga qaytib, keyin rasm chiqardi — ko'zga
 * "chaqnash" bo'lib tashlanadi.
 *
 * Shuning uchun almashish AYNAN haqiqiy manzil tayyor bo'lganda
 * bajariladi.
 */
watch(avatarUrl, (url) => {
  if (url !== null) clearPreview()
})

// Oyna yopilganda osilib qolgan ko'rinish bekor qilinadi (xotira).
onScopeDispose(clearPreview)

/**
 * Oyna ochilganda TOZA holatga qaytadi.
 *
 * ★ Ayniqsa XATO SATRI: tozalanmasa, foydalanuvchi oynani yopib qayta
 * ochganda o'tgan safargi qizil xabar yana ko'rinardi va u hozirgi
 * holatga umuman aloqador bo'lmasdi.
 */
watch(
  () => [props.open, props.user] as const,
  ([isOpen]) => {
    if (!isOpen) return

    avatarError.value = null
    clearPreview()
  },
  { immediate: true },
)

const avatarMutation = useMutation({
  mutationFn: (file: File) => uploadAvatar(file),
  onSuccess: async () => {
    avatarError.value = null

    // ESKI `blob:` manzil bekor qilinadi — u endi hech qayerda
    // ishlatilmaydi va xotirada osilib qolardi.
    if (userId.value !== null) dropAvatar(userId.value, avatarVersion.value)

    // ★ PROFIL DARHOL QAYTA O'QILADI: `avatarUpdatedAt` o'zgaradi,
    //   `useAvatar` yangi kalit bilan rasmni tortadi va u sarlavha
    //   hamda yon menyuda ham almashadi.
    await auth.reloadProfile()
    showToast('Profil rasmi yangilandi')
  },
  onError: (error: Error) => {
    // Yuklash yiqildi — mahalliy ko'rinish OLIB TASHLANADI, aks holda
    // ekranda saqlanmagan rasm turib, foydalanuvchi uni saqlangan deb
    // o'ylardi.
    clearPreview()
    avatarError.value = toUserMessage(error)
    showToast('Rasm yuklanmadi', 'error')
  },
})

const removeMutation = useMutation({
  mutationFn: () => removeAvatar(),
  onSuccess: async () => {
    avatarError.value = null
    clearPreview()
    if (userId.value !== null) dropAvatar(userId.value, avatarVersion.value)
    await auth.reloadProfile()
    showToast('Profil rasmi o‘chirildi')
  },
  onError: (error: Error) => {
    avatarError.value = toUserMessage(error)
  },
})

function pickFile(): void {
  fileInput.value?.click()
}

function handleFile(event: Event): void {
  const input = event.target
  if (!(input instanceof HTMLInputElement)) return

  const file = input.files?.[0]

  // ★ MAYDON DARHOL TOZALANADI: aks holda AYNI faylni ikkinchi marta
  //   tanlash `change` hodisasini umuman bermasdi (qiymat o'zgarmadi).
  input.value = ''

  if (file === undefined) return

  avatarError.value = null

  // Eski ko'rinish bo'lsa (tez-tez almashtirish) — bekor qilinadi.
  clearPreview()
  preview.value = URL.createObjectURL(file)

  avatarMutation.mutate(file)
}

async function handleRemoveAvatar(): Promise<void> {
  const ok = await confirm({
    title: 'Rasmni o‘chirish',
    message: 'Profil rasmi o‘chiriladi va o‘rniga ismingizning bosh harfi ko‘rinadi.',
    confirmLabel: 'O‘chirish',
    tone: 'danger',
  })
  if (!ok) return

  removeMutation.mutate()
}
</script>

<template>
  <BaseModal
    :open="props.open"
    title="Profil rasmi"
    sheet
    @close="emit('close')"
  >
    <div class="space-y-5">
      <!-- ============================================ RASM -->
      <section class="flex flex-col items-center">
        <div class="relative">
          <!--
            Rasm bor bo'lsa — `blob:` manzil (sabab `useAvatar` da),
            aks holda ism harfi. Oraliq "bo'sh kvadrat" holati YO'Q.
          -->
          <img
            v-if="shownAvatar !== null"
            :src="shownAvatar"
            class="size-24 rounded-full object-cover"
            alt=""
          >
          <div
            v-else
            class="flex size-24 items-center justify-center rounded-full bg-gradient-to-br from-brand-500 to-violet-400 text-[36px] font-bold text-white"
            aria-hidden="true"
          >
            {{ initial }}
          </div>

          <!-- Yuklanayotgan payt — rasm ustidagi qatlam. -->
          <div
            v-if="avatarMutation.isPending.value || removeMutation.isPending.value"
            class="absolute inset-0 flex items-center justify-center rounded-full bg-ink-950/60"
          >
            <BaseSpinner size="sm" />
          </div>

          <!--
            Kamera tugmasi — rasmning O'ZI ustida (iOS naqshi). Alohida
            qatorda turgan "Rasm yuklash" tugmasi oynada bekorga bir
            qator joy egallardi.
          -->
          <button
            type="button"
            class="tap-expand absolute -bottom-0.5 -right-0.5 flex size-9 items-center justify-center rounded-full border-2 border-ink-900 bg-brand-500 text-white transition-colors hover:bg-brand-600 disabled:opacity-50"
            :disabled="avatarMutation.isPending.value"
            aria-label="Profil rasmini almashtirish"
            @click="pickFile"
          >
            <!--
              ★ `image`, `camera` EMAS: `icon-names.ts` da `camera` —
              VIDEOQO'NG'IROQ kamerasi (jonli dars boshqaruvi). Bu yerda
              esa gap RASM haqida.
            -->
            <AppIcon
              name="image"
              :size="16"
            />
          </button>
        </div>

        <!--
          ★ `accept="image/*"` — QULAYLIK, HIMOYA EMAS: u faqat fayl
          tanlash oynasidagi ro'yxatni filtrlaydi va uni chetlab o'tish
          oson. HAQIQIY tekshiruv serverda, fayl MAZMUNI bo'yicha.
        -->
        <input
          ref="fileInput"
          type="file"
          accept="image/*"
          class="hidden"
          @change="handleFile"
        >

        <button
          v-if="shownAvatar !== null"
          type="button"
          class="tap-expand mt-3 text-[13px] font-semibold text-rose-500 transition-colors hover:text-rose-400 disabled:opacity-50"
          :disabled="removeMutation.isPending.value"
          @click="handleRemoveAvatar"
        >
          Rasmni o‘chirish
        </button>

        <p
          v-if="avatarError !== null"
          class="mt-2 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-2 text-center text-[12px] leading-relaxed text-rose-400"
          role="alert"
          v-text="avatarError"
        />
      </section>

      <!--
        ★ ISM VA TELEFON ENDI O'QISH-UCHUN-GINA (edit imkoni yo'q) —
        foydalanuvchi bu ma'lumotlar qayerdan kelganini va nega
        o'zgartirib bo'lmasligini bilib tursin.
      -->
      <section class="rounded-2xl border border-line bg-ink-850 p-3.5 text-center">
        <p
          class="text-[15px] font-bold text-slate-100"
          v-text="props.user?.fullName ?? '—'"
        />
        <p class="mt-2 text-[12px] leading-relaxed text-dim">
          Ism va telefon raqamini faqat <b>o‘quv bo‘limi</b> o‘zgartira oladi.
          Xato bo‘lsa — o‘quv bo‘limiga murojaat qiling.
        </p>
      </section>
    </div>

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Yopish
      </BaseButton>
    </template>
  </BaseModal>
</template>
