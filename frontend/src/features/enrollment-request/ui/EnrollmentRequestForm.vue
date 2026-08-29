<script setup lang="ts">
import { computed, ref } from 'vue'

import { submitEnrollmentApplication } from '@/entities/enrollment'
import { toUserMessage } from '@/shared/api'
import {
  maskPhoneField,
  PHONE_INPUT_MAXLENGTH,
  phoneDigits,
  stripPhoneFormatting,
} from '@/shared/lib/phone'
import { AppIcon, BaseButton } from '@/shared/ui'

/*
  ══════════════════════════════════════════════════════════════════════════
  KURSGA ARIZA — LANDING SAHIFADAGI FORMA (2026-08-28)

  🔴 BU RO'YXATDAN O'TISH EMAS. Forma HISOB YARATMAYDI va hech qanday
     kirish huquqi bermaydi — u faqat "biz bilan bog'laning" so'rovi.
     Hisobni hamon FAQAT o'quv bo'limi ochadi.

     Loyiha egasining qarori (2026-08-28): o'z-o'zidan ro'yxatdan o'tish
     yopiq markaz tizimida xavfli — bot ham aynan shu sababdan akkaunt
     yaratmaydi (`TelegramUpdateHandler.HandleContactAsync` izohi).

  ★ MAYDONLAR ATAYLAB KAM: ism va telefon. Har qo'shimcha maydon
    to'ldirilmagan formalar ulushini oshiradi, qolgan ma'lumotni esa
    o'quv bo'limi qo'ng'iroqda baribir so'raydi. "Yo'nalish" va "izoh" —
    IXTIYORIY.
  ══════════════════════════════════════════════════════════════════════════
*/

const props = defineProps<{
  /** Tanlash uchun yo'nalishlar (landing kontentidan). */
  courses: readonly string[]
}>()

const fullName = ref('')
const phone = ref('')
const course = ref('')
const note = ref('')

const isSubmitting = ref(false)
const errorMessage = ref<string | null>(null)

/**
 * Yuborilgandan keyin forma o'rniga rahmat xabari chiqadi.
 *
 * ★ FORMA QAYTA KO'RSATILMAYDI: aks holda foydalanuvchi "ketdimi?" deb
 * ikkinchi marta yuborardi va o'quv bo'limi bitta odamdan ikkita ariza
 * olardi.
 */
const isSent = ref(false)

/** Ism — kamida ikki belgi (bo'sh joylar hisobga olinmaydi). */
const canSubmit = computed(
  () =>
    fullName.value.trim().length >= 2
    && phoneDigits(phone.value).length >= 7
    && !isSubmitting.value,
)

async function handleSubmit(): Promise<void> {
  if (!canSubmit.value) return

  isSubmitting.value = true
  errorMessage.value = null

  try {
    await submitEnrollmentApplication({
      fullName: fullName.value.trim(),
      // ★ RAQAM XOM YUBORILADI — normalizatsiya SERVERDA, `User.NormalizePhone`
      //   bilan. Mijozdagi ikkinchi nusxa ikkalasini asta ajratib yuborardi
      //   (kirish oqimidagi AYNI qoida).
      phone: stripPhoneFormatting(phone.value),
      course: course.value.length > 0 ? course.value : null,
      note: note.value.trim().length > 0 ? note.value.trim() : null,
    })

    isSent.value = true
  } catch (error) {
    // 429 — kvota (bir raqamdan ketma-ket ariza). Boshqa xato holatlari
    // yo'q: server arizani QABUL QILADI va uning taqdiri haqida hech
    // narsa aytmaydi (raqam bazada bormi degan savolga javob bermaslik
    // uchun).
    errorMessage.value = toUserMessage(error)
  } finally {
    isSubmitting.value = false
  }
}
</script>

<template>
  <div
    class="rounded-2xl bg-ink-900 p-6 shadow-lg ring-1 ring-inset ring-line sm:p-8"
  >
    <!-- ─────────────────────────────────────── YUBORILGANDAN KEYIN -->
    <div
      v-if="isSent"
      class="py-6 text-center"
    >
      <div
        class="mx-auto flex size-12 items-center justify-center rounded-2xl bg-green-500/12 text-green-300"
      >
        <AppIcon
          name="check"
          :size="24"
        />
      </div>
      <h3 class="mt-4 text-lg font-semibold text-slate-100">
        Arizangiz qabul qilindi
      </h3>
      <p class="mx-auto mt-2 max-w-sm text-sm leading-relaxed text-slate-400">
        O‘quv bo‘limi ish vaqti davomida siz bilan bog‘lanadi. Iltimos,
        qo‘ng‘iroqni kutib turing.
      </p>
    </div>

    <!-- ───────────────────────────────────────────────────── FORMA -->
    <form
      v-else
      novalidate
      @submit.prevent="handleSubmit"
    >
      <div class="grid gap-4 sm:grid-cols-2">
        <label class="block">
          <span class="mb-1.5 block text-xs font-medium text-slate-400">
            Ism va familiya
          </span>
          <input
            v-model="fullName"
            type="text"
            name="fullName"
            autocomplete="name"
            required
            maxlength="120"
            placeholder="Alisher Karimov"
            class="h-11 w-full rounded-lg bg-ink-950 px-3 text-sm text-slate-100 ring-1 ring-inset ring-line-strong transition-colors placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500"
          >
        </label>

        <label class="block">
          <span class="mb-1.5 block text-xs font-medium text-slate-400">
            Telefon raqami
          </span>
          <div class="relative">
            <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
              <AppIcon
                name="phone"
                :size="17"
              />
            </span>
            <!--
              ★ `:value` + `@input`, `v-model` EMAS — `maskPhoneField`
              izohidagi sabab: `v-model` bilan kursor har bosishda satr
              oxiriga sakrab ketardi.
            -->
            <input
              :value="phone"
              type="tel"
              name="phone"
              inputmode="tel"
              autocomplete="tel"
              required
              :maxlength="PHONE_INPUT_MAXLENGTH"
              placeholder="+998 90 123 45 67"
              class="h-11 w-full rounded-lg bg-ink-950 pl-10 pr-3 text-sm tracking-[0.3px] text-slate-100 ring-1 ring-inset ring-line-strong transition-colors placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500"
              @input="phone = maskPhoneField($event.target as HTMLInputElement)"
            >
          </div>
        </label>
      </div>

      <label class="mt-4 block">
        <span class="mb-1.5 block text-xs font-medium text-slate-400">
          Yo‘nalish <span class="text-slate-600">(ixtiyoriy)</span>
        </span>
        <select
          v-model="course"
          name="course"
          class="h-11 w-full rounded-lg bg-ink-950 px-3 text-sm text-slate-100 ring-1 ring-inset ring-line-strong transition-colors focus:outline-none focus:ring-2 focus:ring-brand-500"
        >
          <option value="">
            Hali tanlamaganman
          </option>
          <option
            v-for="item in props.courses"
            :key="item"
            :value="item"
          >
            {{ item }}
          </option>
        </select>
      </label>

      <label class="mt-4 block">
        <span class="mb-1.5 block text-xs font-medium text-slate-400">
          Izoh <span class="text-slate-600">(ixtiyoriy)</span>
        </span>
        <textarea
          v-model="note"
          name="note"
          rows="3"
          maxlength="500"
          placeholder="Masalan: kechqurungi guruh kerak, 9-sinf o‘quvchisiman"
          class="w-full resize-y rounded-lg bg-ink-950 px-3 py-2.5 text-sm leading-relaxed text-slate-100 ring-1 ring-inset ring-line-strong transition-colors placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500"
        />
      </label>

      <p
        v-if="errorMessage !== null"
        class="mt-4 rounded-xl bg-rose-500/10 px-3 py-2 text-xs text-rose-200 ring-1 ring-inset ring-rose-500/25"
        role="alert"
        v-text="errorMessage"
      />

      <BaseButton
        class="mt-5"
        type="submit"
        size="lg"
        block
        :loading="isSubmitting"
        :disabled="!canSubmit"
      >
        Ariza qoldirish
      </BaseButton>

      <p class="mt-3 text-center text-[12px] leading-relaxed text-slate-500">
        Ma’lumotlaringiz faqat siz bilan bog‘lanish uchun ishlatiladi.
        Ariza yuborish hisob ochmaydi — hisobni o‘quv bo‘limi ochadi.
      </p>
    </form>
  </div>
</template>
