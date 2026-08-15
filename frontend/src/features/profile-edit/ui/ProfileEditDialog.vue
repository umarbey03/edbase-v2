<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, onScopeDispose, ref, watch } from 'vue'

import {
  cancelPhoneChange,
  confirmPhoneChange,
  dropAvatar,
  fetchPhoneChange,
  removeAvatar,
  requestPhoneChange,
  updateProfileName,
  uploadAvatar,
  useAvatar,
} from '@/entities/user'
import type { User } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { toUserMessage } from '@/shared/api'
import {
  formatPhone,
  maskPhoneField,
  PHONE_INPUT_MAXLENGTH,
  phoneDigits,
  stripPhoneFormatting,
} from '@/shared/lib/phone'
import { useConfirm } from '@/shared/lib/useConfirm'
import { showToast } from '@/shared/lib/useToast'
import { AppIcon, BaseButton, BaseField, BaseModal, BaseSpinner } from '@/shared/ui'

/**
 * ============================================================================
 *  PROFILNI TAHRIRLASH (2026-08-15, loyiha egasining talabi)
 * ============================================================================
 *
 * *"Profil oynasida tahrirlash tugmasi ham bo'lsin, bunda yangi modal
 * window ochilsin va bunda user(har qanday userlar) o'z profiliga rasm
 * joylash imkoniyati bo'lsin, ismini o'zgartirishi mumkin bo'lsin,
 * nomerini alishtirish imkoniyati ham bo'lsin — lekin bunda ham
 * registerdagi kabi telegram orqali tasdiqlash majburiy bo'lishi shart."*
 *
 * ── UCHTA MAYDON — UCHTA HAR XIL SAQLASH YO'LI ─────────────────────────────
 *
 * 🔴 BU OYNADA YAGONA "SAQLASH" TUGMASI YO'Q va bu ATAYLAB:
 *
 *   • RASM    — tanlangan zahoti yuklanadi (fayl tanlash oynasi
 *               foydalanuvchi uchun allaqachon "tasdiq");
 *   • ISM     — "Saqlash" tugmasi bilan (matn maydoni, xatosi arzon);
 *   • TELEFON — IKKI BOSQICHLI oqim, Telegram tasdig'i bilan.
 *
 * Ularni bitta tugma ostiga yig'ish IMKONSIZ edi: telefon oqimi ilovadan
 * CHIQIB Telegramga o'tishni talab qiladi, ya'ni "saqlash" bosilgan
 * paytda u hali tugamagan bo'ladi. Bitta tugma bo'lsa, ism saqlanib,
 * telefon esa jimgina saqlanmay qolardi.
 *
 * ── TELEFON OQIMI (uch holat) ──────────────────────────────────────────────
 *
 *   1) `pending === null`        — raqam kiritish maydoni;
 *   2) `pending.codeSent === false` — "botga raqamni ulashing" ko'rsatmasi
 *      (ilova bu paytda holatni QISQA oraliqlarda so'rab turadi —
 *      foydalanuvchi Telegramda tugmani bosganini boshqa yo'l bilan
 *      bilib bo'lmaydi);
 *   3) `pending.codeSent === true`  — kod kiritish maydoni.
 */
const props = defineProps<{
  open: boolean
  user: User | null
}>()

const emit = defineEmits<{ close: [] }>()

const queryClient = useQueryClient()
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
 * ★ AYNI XATO ISMDA HAM BOR EDI, faqat ko'zga kamroq tashlanardi:
 *   `nameDirty` yangi qiymatni ESKI `props.user.fullName` bilan
 *   solishtirardi, ya'ni saqlangandan keyin ham "Ismni saqlash" tugmasi
 *   FAOL qolaverardi.
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

/* --------------------------------- ism ---------------------------------- */

const fullName = ref('')
const nameError = ref<string | null>(null)

/* -------------------------------- rasm ---------------------------------- */

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

/* ------------------------------- telefon -------------------------------- */

const newPhone = ref('')
const code = ref('')
const phoneError = ref<string | null>(null)

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
 * ★ Ayniqsa XATO SATRLARI: ular tozalanmasa, foydalanuvchi oynani yopib
 * qayta ochganda o'tgan safargi qizil xabar yana ko'rinardi va u
 * hozirgi holatga umuman aloqador bo'lmasdi.
 */
watch(
  () => [props.open, props.user] as const,
  ([isOpen]) => {
    if (!isOpen) return

    fullName.value = props.user?.fullName ?? ''
    newPhone.value = ''
    code.value = ''
    nameError.value = null
    avatarError.value = null
    phoneError.value = null
    clearPreview()
  },
  { immediate: true },
)

/* ============================== ism: saqlash ============================= */

const nameMutation = useMutation({
  mutationFn: () => updateProfileName(fullName.value.trim()),
  onSuccess: async () => {
    nameError.value = null
    await auth.reloadProfile()
    showToast('Ism saqlandi')
  },
  onError: (error: Error) => {
    nameError.value = toUserMessage(error)
    showToast('Ism saqlanmadi', 'error')
  },
})

const nameDirty = computed(
  () => fullName.value.trim().length > 0 && fullName.value.trim() !== (props.user?.fullName ?? ''),
)

function saveName(): void {
  if (!nameDirty.value || nameMutation.isPending.value) return
  nameError.value = null
  nameMutation.mutate()
}

/* ============================= rasm: yuklash ============================= */

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

/* =========================== telefon: 1-bosqich ========================== */

/**
 * Kutayotgan almashtirish.
 *
 * ★ `refetchInterval` — FAQAT bot javobini kutayotganda (3 soniya).
 * Foydalanuvchi Telegramda tugmani bosganini ilova boshqa yo'l bilan
 * BILA OLMAYDI: hub kanali bu hodisani tarqatmaydi va uni qo'shish
 * butun realtime infratuzilmasiga bitta ekran uchun yangi hodisa turi
 * qo'shish degani bo'lardi.
 *
 * Kod kelgach (`codeSent === true`) so'rov TO'XTAYDI — kutadigan narsa
 * qolmadi.
 */
const pendingQuery = useQuery({
  queryKey: ['profile', 'phone-change'],
  queryFn: ({ signal }) => fetchPhoneChange({ signal }),
  enabled: computed(() => props.open),
  refetchInterval: (query) => (query.state.data?.codeSent === false ? 3000 : false),
})

const pending = computed(() => pendingQuery.data.value ?? null)

const requestMutation = useMutation({
  mutationFn: () => requestPhoneChange(stripPhoneFormatting(newPhone.value)),
  onSuccess: (status) => {
    phoneError.value = null
    queryClient.setQueryData(['profile', 'phone-change'], status)
    showToast('Endi botga yangi raqamdan «Raqamni ulashish» yuboring', 'info')
  },
  onError: (error: Error) => {
    phoneError.value = toUserMessage(error)
  },
})

const confirmMutation = useMutation({
  mutationFn: () => confirmPhoneChange(code.value.trim()),
  onSuccess: async () => {
    phoneError.value = null
    code.value = ''
    newPhone.value = ''
    queryClient.setQueryData(['profile', 'phone-change'], null)
    await auth.reloadProfile()
    showToast('Telefon raqami almashtirildi')
  },
  onError: (error: Error) => {
    phoneError.value = toUserMessage(error)
  },
})

const cancelMutation = useMutation({
  mutationFn: () => cancelPhoneChange(),
  onSuccess: () => {
    phoneError.value = null
    code.value = ''
    queryClient.setQueryData(['profile', 'phone-change'], null)
  },
  onError: (error: Error) => {
    phoneError.value = toUserMessage(error)
  },
})

/** Kamida 7 raqam — `LoginPage` dagi AYNI yumshoq filtr (chet el raqami uchun). */
const canRequest = computed(
  () => phoneDigits(newPhone.value).length >= 7 && !requestMutation.isPending.value,
)

const canConfirm = computed(
  () => /^\d{6}$/u.test(code.value.trim()) && !confirmMutation.isPending.value,
)

function submitPhone(): void {
  if (!canRequest.value) return
  phoneError.value = null
  requestMutation.mutate()
}

function submitCode(): void {
  if (!canConfirm.value) return
  phoneError.value = null
  confirmMutation.mutate()
}

async function handleCancelPhone(): Promise<void> {
  const ok = await confirm({
    title: 'Almashtirishni bekor qilish',
    message: 'Telefon raqamini almashtirish so‘rovi bekor qilinadi.',
    confirmLabel: 'Bekor qilish',
    cancelLabel: 'Davom etish',
    tone: 'warning',
  })
  if (!ok) return

  cancelMutation.mutate()
}

/** Botga o'tish havolasi (`t.me/...`). Bot nomi sozlanmagan bo'lsa `null`. */
const botLink = computed(() => {
  const name = pending.value?.botUsername ?? null
  return name === null ? null : `https://t.me/${name}`
})
</script>

<template>
  <BaseModal
    :open="props.open"
    title="Profilni tahrirlash"
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

      <!-- ============================================ ISM -->
      <section>
        <BaseField label="To‘liq ism">
          <input
            v-model="fullName"
            class="zn-input"
            autocomplete="name"
            maxlength="200"
            @keydown.enter.prevent="saveName"
          >
        </BaseField>

        <p
          v-if="nameError !== null"
          class="mt-2 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-2 text-[12px] leading-relaxed text-rose-400"
          role="alert"
          v-text="nameError"
        />

        <BaseButton
          class="mt-2.5"
          variant="primary"
          block
          :disabled="!nameDirty"
          :loading="nameMutation.isPending.value"
          @click="saveName"
        >
          Ismni saqlash
        </BaseButton>
      </section>

      <!-- ============================================ TELEFON -->
      <section class="rounded-2xl border border-line bg-ink-850 p-3.5">
        <h3 class="text-[13px] font-bold text-slate-200">
          Telefon raqami
        </h3>
        <p class="mt-1 text-[12px] leading-relaxed text-dim">
          Joriy raqam:
          <span
            class="font-semibold text-slate-300 tabular-nums"
            v-text="formatPhone(props.user?.phone) || 'kiritilmagan'"
          />
        </p>

        <!-- ---- 1-BOSQICH: yangi raqam ---- -->
        <template v-if="pending === null">
          <div class="mt-3">
            <BaseField label="Yangi raqam">
              <input
                :value="newPhone"
                class="zn-input tabular-nums"
                type="tel"
                inputmode="tel"
                autocomplete="tel"
                :maxlength="PHONE_INPUT_MAXLENGTH"
                placeholder="+998 90 123 45 67"
                @input="newPhone = maskPhoneField($event.target as HTMLInputElement)"
                @keydown.enter.prevent="submitPhone"
              >
            </BaseField>
          </div>

          <BaseButton
            class="mt-2.5"
            variant="secondary"
            block
            :disabled="!canRequest"
            :loading="requestMutation.isPending.value"
            @click="submitPhone"
          >
            Raqamni almashtirish
          </BaseButton>
        </template>

        <!-- ---- 2-BOSQICH: botga raqamni ulash ---- -->
        <template v-else-if="!pending.codeSent">
          <div class="mt-3 rounded-xl border border-amber-500/25 bg-amber-500/10 p-3">
            <p class="text-[12px] font-semibold text-amber-400">
              Telegram tasdig‘i kutilmoqda
            </p>
            <p class="mt-1.5 text-[12px] leading-relaxed text-slate-300">
              <span
                class="font-semibold tabular-nums"
                v-text="formatPhone(pending.phone)"
              />
              raqamini tasdiqlash uchun <b>o‘sha raqam ulangan Telegram
                hisobidan</b> botga kiring va «Raqamni ulashish» tugmasini
              bosing. Kod o‘sha hisobga keladi.
            </p>

            <a
              v-if="botLink !== null"
              :href="botLink"
              target="_blank"
              rel="noopener noreferrer"
              class="tap-target mt-2.5 inline-flex items-center gap-1.5 rounded-lg bg-brand-500 px-3 py-2 text-[12px] font-semibold text-on-brand transition-colors hover:bg-brand-600"
            >
              <AppIcon
                name="send"
                :size="14"
              />
              Botni ochish
            </a>

            <p class="mt-2 flex items-center gap-1.5 text-[11px] text-dim">
              <BaseSpinner size="sm" />
              Tugmani bosishingizni kutmoqdamiz…
            </p>
          </div>

          <button
            type="button"
            class="tap-expand mt-2.5 text-[12px] font-semibold text-slate-400 transition-colors hover:text-rose-400"
            @click="handleCancelPhone"
          >
            So‘rovni bekor qilish
          </button>
        </template>

        <!-- ---- 3-BOSQICH: kod ---- -->
        <template v-else>
          <div class="mt-3">
            <BaseField label="Telegramga kelgan kod">
              <input
                v-model="code"
                class="zn-input text-center text-lg font-bold tracking-[6px] tabular-nums"
                inputmode="numeric"
                autocomplete="one-time-code"
                maxlength="6"
                placeholder="000000"
                @keydown.enter.prevent="submitCode"
              >
            </BaseField>
            <p class="mt-1.5 text-[11px] leading-relaxed text-dim">
              Kod
              <span
                class="font-semibold tabular-nums"
                v-text="formatPhone(pending.phone)"
              />
              raqamiga ulangan Telegram hisobiga yuborildi.
            </p>
          </div>

          <BaseButton
            class="mt-2.5"
            variant="primary"
            block
            :disabled="!canConfirm"
            :loading="confirmMutation.isPending.value"
            @click="submitCode"
          >
            Tasdiqlash va almashtirish
          </BaseButton>

          <button
            type="button"
            class="tap-expand mt-2.5 text-[12px] font-semibold text-slate-400 transition-colors hover:text-rose-400"
            @click="handleCancelPhone"
          >
            So‘rovni bekor qilish
          </button>
        </template>

        <p
          v-if="phoneError !== null"
          class="mt-2.5 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-2 text-[12px] leading-relaxed text-rose-400"
          role="alert"
          v-text="phoneError"
        />
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
