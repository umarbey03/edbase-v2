<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { createTest, TEST_TITLE_MAX, testKindLabel, testTitle, updateTest } from '@/entities/test'
import { toUserMessage } from '@/shared/api'
import { fromDateTimeLocalInput, toDateTimeLocalInput } from '@/shared/lib/datetime'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { CreateTestRequest, TestDto, TestKindName, UpdateTestRequest } from '@/shared/types'
import { BaseButton, BaseDrawer, BaseField } from '@/shared/ui'

import TestLessonPicker from './TestLessonPicker.vue'

/**
 * Testni yaratish/tahrirlash (o'quv bo'limi/admin).
 *
 * ★★ TAHRIRLASH — TO'LIQ ALMASHTIRISH ★★
 * `PUT /tests/{id}` C# DTO'sida ixtiyoriy maydonlar `= null` standart qiymatga
 * ega va `TestService.UpdateAsync` ularni to'g'ridan-to'g'ri yozadi
 * (`test.TimeLimitMinutes = request.TimeLimitMinutes`). Ya'ni FORMADA
 * YUBORILMAGAN maydon serverda JIMGINA o'chadi: tavsif, vaqt chegarasi va
 * muddat yo'qolardi. Shuning uchun forma ochilganda mavjud qiymatlar to'liq
 * yuklanadi va saqlashda HAMMASI qaytariladi. Aynan shu tuzoq bugun guruh
 * formasida kursni o'chirib yuborgan edi.
 *
 * TUR (`kind`) va DARS faqat YARATISHDA tanlanadi: server ularni tahrirlashda
 * ATAYLAB o'zgartirmaydi — musobaqa testini dars testiga aylantirish gating'ga
 * ta'sir qilardi va allaqachon topshirgan o'quvchilarning natijasini boshqa
 * ma'noga o'tkazardi.
 */
const props = defineProps<{
  open: boolean
  /** `null` — yangi test rejimi. */
  test: TestDto | null
}>()

const emit = defineEmits<{ close: []; saved: [test: TestDto] }>()

const isEdit = computed(() => props.test !== null)

const title = ref('')
const description = ref('')
const kind = ref<TestKindName>('Competition')
const lessonId = ref<number | null>(null)
const timeLimitText = ref('')
const dueLocal = ref('')
const errorMessage = ref<string | null>(null)

function resetForm(): void {
  const test = props.test
  title.value = test?.title ?? ''
  description.value = test?.description ?? ''
  kind.value = test?.kind ?? 'Competition'
  lessonId.value = test?.moduleLessonId ?? null
  timeLimitText.value = test?.timeLimitMinutes === null ? '' : String(test?.timeLimitMinutes ?? '')
  dueLocal.value = toDateTimeLocalInput(test?.dueAt ?? null)
  errorMessage.value = null
}

watch(() => [props.open, props.test], resetForm, { immediate: true })

/* ---------------------------------------------------------- tekshiruvlar */

const trimmedTitle = computed(() => title.value.trim())

const titleError = computed<string | null>(() =>
  trimmedTitle.value.length > TEST_TITLE_MAX ? `Sarlavha ${TEST_TITLE_MAX} belgidan oshmasin.` : null,
)

/**
 * Vaqt chegarasi — BO'SH bo'lsa "chegarasiz" (`null`).
 *
 * Domain: `TimeLimitMinutes` berilgan bo'lsa noldan katta bo'lishi shart,
 * aks holda 409.
 */
const parsedTimeLimit = computed<number | null>(() => {
  const raw = timeLimitText.value.trim()
  if (raw.length === 0) return null
  const value = Number(raw)
  return Number.isFinite(value) ? value : Number.NaN
})

const timeLimitError = computed<string | null>(() => {
  const value = parsedTimeLimit.value
  if (value === null) return null
  if (!Number.isFinite(value)) return 'Vaqt chegarasi raqam bo‘lishi kerak.'
  if (!Number.isInteger(value)) return 'Vaqt chegarasi butun daqiqa bo‘lishi kerak.'
  if (value <= 0) return 'Vaqt chegarasi noldan katta bo‘lishi kerak.'
  return null
})

/** Dars testida dars SHART, musobaqada esa bo'lmasligi shart (`Test.Validate`). */
const targetError = computed<string | null>(() => {
  if (isEdit.value) return null
  if (kind.value === 'Lesson' && lessonId.value === null) return 'Dars testi uchun darsni tanlang.'
  return null
})

/* ------------------------------------------------------------- saqlash */

/**
 * `PUT` tanasi — BARCHA maydon qaytariladi (yuqoridagi izoh).
 * `title` bo'sh matnga tushib qolmasligi uchun forma uni majburiy qiladi.
 */
function updatePayload(): UpdateTestRequest {
  const text = description.value.trim()
  return {
    title: trimmedTitle.value,
    description: text.length > 0 ? text : null,
    timeLimitMinutes: parsedTimeLimit.value,
    dueAt: fromDateTimeLocalInput(dueLocal.value),
  }
}

function createPayload(): CreateTestRequest {
  return {
    ...updatePayload(),
    kind: kind.value,
    // Musobaqa testi darsga BOG'LANMAYDI — aks holda server 409 beradi.
    moduleLessonId: kind.value === 'Lesson' ? lessonId.value : null,
  }
}

const createMutation = useMutation({
  mutationFn: () => createTest(createPayload()),
  onSuccess: (test) => {
    emit('saved', test)
    emit('close')
  },
  onError: (error: Error) => {
    // 409 — Domain qoidasi (sarlavha, vaqt chegarasi, tur <-> dars muvofiqligi);
    // 404 — ko'rsatilgan dars topilmadi; 403 — rol yetarli emas.
    errorMessage.value = toUserMessage(error)
  },
})

const updateMutation = useMutation({
  mutationFn: (id: number) => updateTest(id, updatePayload()),
  onSuccess: (test) => {
    emit('saved', test)
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const isPending = computed(() => createMutation.isPending.value || updateMutation.isPending.value)

const canSubmit = computed(
  () =>
    trimmedTitle.value.length > 0 &&
    titleError.value === null &&
    timeLimitError.value === null &&
    targetError.value === null &&
    !isPending.value,
)

const confirm = useConfirm()

/**
 * R4 — TASDIQ FAQAT TAHRIRLASHDA (B2 jadvali: ma'lumotni ALMASHTIRUVCHI
 * saqlash → `primary`). Yaratishda so'ralmaydi: yangi test hech narsani
 * almashtirmaydi va e'lon qilinmagunicha o'quvchi uni ko'rmaydi.
 *
 * ★ MATN AYNAN "ALMASHTIRILADI" DEYDI, chunki `PUT` shu faylning boshidagi
 * izohda yozilgan tuzoqni saqlaydi: server yuborilmagan maydonni JIMGINA
 * `null` ga tushiradi. Forma hozir hammasini qaytaradi, lekin tasdiq matni
 * foydalanuvchiga aynan shu xulqni aytadi — ya'ni "men faqat sarlavhani
 * o'zgartirdim" degan taxminni oldindan buzadi.
 *
 * ★ E'LON QILINGAN TESTDA QO'SHIMCHA QATOR: o'quvchi ayni damda testni
 * yechayotgan bo'lishi mumkin va vaqt chegarasi/muddat o'zgarishi uning
 * ochiq urinishiga tegadi.
 */
async function handleSubmit(): Promise<void> {
  if (!canSubmit.value) return

  const test = props.test
  if (test !== null) {
    const details = [
      'Tavsif, vaqt chegarasi va muddat formadagi qiymatlar bilan qayta yoziladi.',
      'Savollar va topshirilgan natijalar tegilmaydi.',
    ]
    if (test.isPublished) {
      details.unshift('Test E’LON QILINGAN — o‘zgarish o‘quvchilarga darhol ko‘rinadi.')
    }

    const ok = await confirm({
      title: 'Testni saqlash',
      message: `“${testTitle(test)}” ma’lumotlari ALMASHTIRILADI.`,
      confirmLabel: 'Saqlash',
      tone: 'primary',
      details,
    })
    if (!ok) return
  }

  errorMessage.value = null
  if (test !== null) updateMutation.mutate(test.id)
  else createMutation.mutate()
}
</script>

<template>
  <!--
    🔴 `BaseModal` -> `BaseDrawer` (loyiha egasi, 2026-08-15: "test yaratish
    modali ekranni o'ng tarafidan 85%ini egallab ochilishi kerak"). API bir
    xil (`open`/`title`/`@close` + `#footer`), shuning uchun forma mantig'i
    TEGILMAGAN — faqat konteyner almashdi.
  -->
  <BaseDrawer
    :open="props.open"
    :title="isEdit ? 'Testni tahrirlash' : 'Yangi test'"
    @close="emit('close')"
  >
    <form
      novalidate
      @submit.prevent="handleSubmit"
    >
      <!-- TUR: yaratishda tanlanadi, tahrirlashda faqat ko'rsatiladi. -->
      <template v-if="!isEdit">
        <div
          class="mb-3 flex gap-2"
          role="group"
          aria-label="Test turi"
        >
          <button
            type="button"
            class="tap-target flex-1 rounded-lg border px-3 text-xs font-medium transition-colors"
            :class="
              kind === 'Competition'
                ? 'border-brand-500 bg-brand-500/16 text-brand-400'
                : 'border-line bg-ink-800 text-slate-300 hover:bg-ink-750'
            "
            @click="kind = 'Competition'"
          >
            Musobaqa
          </button>
          <button
            type="button"
            class="tap-target flex-1 rounded-lg border px-3 text-xs font-medium transition-colors"
            :class="
              kind === 'Lesson'
                ? 'border-brand-500 bg-brand-500/16 text-brand-400'
                : 'border-line bg-ink-800 text-slate-300 hover:bg-ink-750'
            "
            @click="kind = 'Lesson'"
          >
            Dars testi
          </button>
        </div>

        <TestLessonPicker
          v-if="kind === 'Lesson'"
          v-model="lessonId"
          :enabled="props.open"
        />
        <p
          v-else
          class="mb-3 rounded-lg border border-line bg-ink-950 px-3 py-2 text-[11px] leading-relaxed text-slate-400"
        >
          Musobaqa testi kursdan MUSTAQIL: u sur‘at nazoratiga kirmaydi va
          e‘lon qilinishi bilan barcha o‘quvchilarga ko‘rinadi.
        </p>

        <p
          v-if="targetError !== null"
          class="mt-1 text-[11px] text-rose-400"
          v-text="targetError"
        />

        <hr class="my-4 border-line">
      </template>

      <div
        v-else-if="props.test !== null"
        class="mb-4 rounded-lg border border-line bg-ink-950 p-3"
      >
        <p class="text-xs font-medium text-slate-200">
          {{ testKindLabel(props.test.kind) }}
          <span
            v-if="props.test.moduleLessonName !== null"
            class="text-slate-400"
          >· {{ props.test.moduleLessonName }}</span>
        </p>
        <p class="mt-1 text-[11px] leading-relaxed text-dim">
          Tur va dars o‘zgartirilmaydi: musobaqa testini dars testiga aylantirish
          sur‘at nazoratiga ta’sir qilardi va topshirilgan natijalar boshqa
          ma’noga o‘tardi. Boshqa nishon kerak bo‘lsa — yangi test yarating.
        </p>
      </div>

      <BaseField
        label="Sarlavha"
        :error="titleError"
      >
        <input
          v-model="title"
          class="zn-input"
          required
          placeholder="Masalan: 3-modul yakuniy testi"
        >
      </BaseField>

      <div class="mt-3">
        <BaseField
          label="Tavsif"
          hint="Ixtiyoriy. O‘quvchi boshlash ekranida ko‘radi."
        >
          <textarea
            v-model="description"
            class="zn-input min-h-24 resize-y"
            rows="3"
          />
        </BaseField>
      </div>

      <div class="mt-3 grid gap-3 sm:grid-cols-2">
        <BaseField
          label="Vaqt chegarasi (daqiqa)"
          hint="Bo‘sh bo‘lsa — chegarasiz."
          :error="timeLimitError"
        >
          <input
            v-model="timeLimitText"
            class="zn-input"
            inputmode="numeric"
            placeholder="30"
          >
        </BaseField>

        <BaseField
          label="Topshirish muddati"
          hint="Bo‘sh bo‘lsa — muddatsiz."
        >
          <input
            v-model="dueLocal"
            class="zn-input"
            type="datetime-local"
          >
        </BaseField>
      </div>

      <p class="mt-1 text-[11px] leading-relaxed text-dim">
        Ikkala chegara ham SERVERDA tekshiriladi va ERTAROG‘I kuchda bo‘ladi:
        30 daqiqalik test 18:00 muddat bilan 17:59 da boshlansa, o‘quvchining
        taymeri 18:00 ni ko‘rsatadi.
      </p>

      <p
        v-if="errorMessage !== null"
        class="mt-3 text-xs text-rose-400"
        role="alert"
        v-text="errorMessage"
      />
    </form>

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Bekor qilish
      </BaseButton>
      <BaseButton
        :disabled="!canSubmit"
        :loading="isPending"
        @click="handleSubmit"
      >
        {{ isEdit ? 'Saqlash' : 'Yaratish' }}
      </BaseButton>
    </template>
  </BaseDrawer>
</template>
