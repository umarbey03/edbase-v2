<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'

import {
  assignmentTitle,
  fetchSubmissions,
  gradeSubmission,
  reopenSubmission,
} from '@/entities/assignment'
import SubmissionAttachments from '@/entities/assignment/ui/SubmissionAttachments.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import type { AssignmentDto, SubmissionDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseModal, DataStatus } from '@/shared/ui'

import { QUICK_FEEDBACK, quickGradeOptions } from '../model/queue'
import { useQueueShortcuts } from '../model/use-queue-shortcuts'

/**
 * TEKSHIRISH NAVBATI — to'liq ekran, bitta ish, tez baho, keyingisiga o'tish.
 *
 * NEGA MODAL EMAS, TO'LIQ EKRAN: bu ustozning kunlik ASOSIY ishi. Eski
 * ilovada (`teacher.html`, `#qv` bloki) u to'liq ekranni egallaydi va
 * o'nlab ishni klaviaturadan chiqmasdan baholash mumkin. v2 dagi "kartochka
 * bosib modal ochish" oqimi har ish uchun ikki qo'shimcha bosish talab
 * qilardi — kuniga 100 ta ishda bu 200 ta ortiqcha harakat.
 *
 * ★ NAVBAT QAMROVI: BITTA VAZIFA BO'YICHA.
 * Serverda "ustozning barcha kutilayotgan ishlari" endpoint'i YO'Q — mavjud
 * `GET /assignments/{id}/submissions` faqat bitta vazifa kesimida ishlaydi.
 * Shu sababli ustoz avval vazifani tanlaydi, so'ng navbatga kiradi. Eski
 * ilovada navbat barcha vazifalarni qamrab olardi (`/api/teacher/queue`) —
 * bu farq backend endpoint'i qo'shilgach yopiladi va bu komponentning
 * ichki mantig'i o'zgarmaydi (faqat ma'lumot manbai almashadi).
 *
 * ★ ISHCHI NUSXA (`queue`) — server javobining MAHALLIY nusxasi.
 * Baholangan ish navbatdan darhol chiqadi (eski ilovaning
 * `QUEUE.splice(QI,1)` xulqi), aks holda ro'yxat qayta yuklanguncha ustoz
 * o'zi baholagan ishni yana ko'rib turardi va indeks sakrab ketardi.
 * Server holati esa TanStack Query'da qoladi va `changed` orqali
 * yangilanadi — ortidagi sahifa sanoqlari eskirmasin.
 */
const props = defineProps<{ assignment: AssignmentDto }>()

const emit = defineEmits<{ close: []; changed: [] }>()

/* Kalit sahifadagi so'rov kaliti bilan BIR XIL — ro'yxat qayta yuklanmaydi. */
const submissionsQuery = useQuery({
  queryKey: computed(() => ['assignment-submissions', props.assignment.id]),
  queryFn: ({ signal }) => fetchSubmissions(props.assignment.id, { signal }),
})

const queue = ref<SubmissionDto[]>([])
/** Seans boshidagi ishlar soni — progress shkalasi shunga nisbatan chiziladi. */
const initialCount = ref(0)
const index = ref(0)
/** Ishchi nusxa BIR MARTA yig'iladi: fon yangilanishi indeksni surib yubormasin. */
let queueBuilt = false

watch(
  () => submissionsQuery.data.value,
  (list) => {
    if (queueBuilt || list === undefined) return
    queueBuilt = true
    // Navbatda FAQAT baholanmaganlar — eski ilovada ham shunday edi
    // ("Baholanmagan javob qolmadi").
    queue.value = list.filter((item) => item.status !== 'Graded')
    initialCount.value = queue.value.length
  },
  { immediate: true },
)

const current = computed<SubmissionDto | null>(() => queue.value[index.value] ?? null)

const loadError = computed(() =>
  submissionsQuery.error.value !== null ? toUserMessage(submissionsQuery.error.value) : null,
)

/* ---------------------------------------------------------------- baholash */

const gradeOptions = computed(() => quickGradeOptions(props.assignment.maxScore))

/*
  Ball YAGONA manbadan boshqariladi: tugmalar ham, "boshqa ball" maydoni ham
  shu satrni yozadi. Ikki alohida holat (tanlangan tugma + qo'lda kiritilgan
  son) bo'lsa ular bir-biriga zid qolishi mumkin edi.
*/
const scoreText = ref('')
const feedback = ref('')
const formError = ref<string | null>(null)
const savedNotice = ref<string | null>(null)

// Vergul bilan yozilgan ball ("4,5") ham qabul qilinadi — o'zbek klaviaturasida odatiy.
const parsedScore = computed(() => Number(scoreText.value.replace(',', '.')))

const scoreError = computed<string | null>(() => {
  if (scoreText.value.trim().length === 0) return null
  if (!Number.isFinite(parsedScore.value)) return 'Ball raqam bo‘lishi kerak.'
  if (parsedScore.value < 0) return 'Ball manfiy bo‘lmaydi.'
  if (parsedScore.value > props.assignment.maxScore) {
    return `Maksimal ball: ${props.assignment.maxScore}.`
  }
  return null
})

const hasScore = computed(
  () => scoreText.value.trim().length > 0 && scoreError.value === null,
)

const bodyElement = ref<HTMLElement | null>(null)
const rootElement = ref<HTMLElement | null>(null)
const feedbackInput = ref<HTMLInputElement | null>(null)
/** Kattalashtirilgan rasm manzili (`null` — oyna yopiq). */
const zoomUrl = ref<string | null>(null)

// Keyingi ishga o'tilganda maydonlar oldingi ishning qiymatida qolmasin.
watch(current, () => {
  const item = current.value
  scoreText.value = item !== null && item.score !== null ? String(item.score) : ''
  feedback.value = item?.feedback ?? ''
  formError.value = null
  zoomUrl.value = null
  bodyElement.value?.scrollTo({ top: 0 })
})

interface GradePayload {
  id: number
  score: number
  feedback: string
  studentName: string
}

const gradeMutation = useMutation({
  mutationFn: (payload: GradePayload) =>
    gradeSubmission(payload.id, {
      score: payload.score,
      feedback: payload.feedback.length > 0 ? payload.feedback : null,
    }),
  onSuccess: (_result, payload) => {
    savedNotice.value = `${payload.studentName} — ${payload.score} ball saqlandi`
    removeCurrent()
    emit('changed')
  },
  onError: (error: Error) => {
    // 400 da sabab `problem.errors` da, 409 da `detail` da — `toUserMessage`
    // ikkalasini ham to'g'ri o'qiydi.
    formError.value = toUserMessage(error)
  },
})

const reopenMutation = useMutation({
  mutationFn: (payload: { id: number; note: string; studentName: string }) =>
    reopenSubmission(payload.id, { note: payload.note }),
  onSuccess: (_result, payload) => {
    savedNotice.value = `${payload.studentName} — qayta topshirishga qaytarildi`
    removeCurrent()
    emit('changed')
  },
  onError: (error: Error) => {
    formError.value = toUserMessage(error)
  },
})

const isBusy = computed(() => gradeMutation.isPending.value || reopenMutation.isPending.value)

/**
 * Ishni navbatdan olib tashlaydi.
 *
 * INDEKS JOYIDA QOLADI: o'chirilgan o'rinni keyingi ish egallaydi, ya'ni
 * ustoz avtomatik ravishda navbatdagi keyingisini ko'radi (eski ilovadagi
 * `splice` xulqi). Oxirgi ish baholansa indeks ro'yxatdan chiqadi va
 * "navbat tugadi" ekrani ochiladi.
 */
function removeCurrent(): void {
  queue.value.splice(index.value, 1)
  if (index.value > queue.value.length) index.value = queue.value.length
}

function pick(value: number): void {
  // Tugmasi yo'q raqam e'tiborsiz qoldiriladi (eski ilovada ham `data-v`
  // bo'yicha tugma topilmasa hech narsa bo'lmasdi).
  if (!gradeOptions.value.includes(value)) return
  scoreText.value = String(value)
  formError.value = null
}

function save(): void {
  const item = current.value
  if (item === null) return

  /*
    ★ IKKI MARTA BAHOLASHNING OLDINI OLISH.
    `Enter` bosib turgan ustoz so'rov ketayotganda ikkinchisini yuborardi va
    server ikkinchi urinishda 409 ("allaqachon baholangan") qaytarardi.
  */
  if (isBusy.value) return

  if (!hasScore.value) {
    formError.value = scoreError.value ?? 'Avval baho tanlang.'
    return
  }

  formError.value = null
  savedNotice.value = null
  gradeMutation.mutate({
    id: item.id,
    score: parsedScore.value,
    feedback: feedback.value.trim(),
    studentName: item.studentName ?? 'O‘quvchi',
  })
}

/** "Qaytarish" — o'quvchi ishini tuzatib qayta yuboradi. */
function reopen(): void {
  const item = current.value
  if (item === null || isBusy.value) return

  const note = feedback.value.trim()
  if (note.length === 0) {
    // Eski ilovadagi qoida: sababsiz qaytarilgan ish o'quvchida "nima
    // qilishim kerak?" degan savol qoldiradi va u ustozga yozadi.
    formError.value = 'Izoh yozing — o‘quvchi nimani tuzatishini bilishi kerak.'
    feedbackInput.value?.focus()
    return
  }

  formError.value = null
  savedNotice.value = null
  reopenMutation.mutate({ id: item.id, note, studentName: item.studentName ?? 'O‘quvchi' })
}

/** O'tkazib yuborish: ish navbatda QOLADI, faqat keyinroq ko'riladi. */
function next(): void {
  if (current.value === null || isBusy.value) return
  savedNotice.value = null
  index.value += 1
}

function restart(): void {
  savedNotice.value = null
  index.value = 0
}

/**
 * `Space` — birinchi audio javobni ijro/pauza.
 *
 * Element DOM'dan qidiriladi (eski ilovadagidek): audio pleyer `entities`
 * qatlamidagi komponent ichida va u navbat mantig'idan bexabar bo'lishi
 * kerak — uni boshqarish uchun butun daraxt bo'ylab `ref` uzatish
 * bog'liqlikni teskari yo'nalishga burardi.
 */
function toggleAudio(): void {
  const audio = bodyElement.value?.querySelector('audio')
  if (audio === null || audio === undefined) return
  if (audio.paused) {
    // Brauzer avtomatik ijroni rad etishi mumkin — bu xato emas.
    void audio.play().catch(() => undefined)
  } else {
    audio.pause()
  }
}

function close(): void {
  emit('close')
}

/*
  Kattalashtirilgan rasm ochiq bo'lganda yorliqlar O'CHADI: aks holda `Esc`
  bir vaqtda rasm oynasini ham, butun navbatni ham yopardi (ikkala tinglovchi
  ham `document` da o'tiradi).
*/
const shortcutsActive = computed(() => zoomUrl.value === null)

useQueueShortcuts(shortcutsActive, {
  onDigit: pick,
  onSave: save,
  onNext: next,
  onToggleAudio: toggleAudio,
  onClose: close,
})

/* --------------------------------------------------------------- ko'rinish */

const position = computed(() => Math.min(index.value + 1, queue.value.length))

/**
 * Bajarilgan ish ulushi. Maxraj SEANS BOSHIDAGI son: `queue` uzunligi har
 * baholashda kamayadi va undan foydalansak shkala joyida turib qolardi.
 */
const progressPercent = computed(() =>
  initialCount.value === 0
    ? 100
    : Math.round(((initialCount.value - queue.value.length) / initialCount.value) * 100),
)

const doneTitle = computed(() =>
  queue.value.length === 0 ? 'Hammasi tekshirildi' : 'Navbat oxiri',
)

const doneText = computed(() =>
  queue.value.length === 0
    ? 'Baholanmagan javob qolmadi.'
    : `${queue.value.length} ta ish o‘tkazib yuborilgan — ularni “Boshidan” tugmasi bilan qayta ko‘ring.`,
)

const answerIsEmpty = computed(() => {
  const item = current.value
  if (item === null) return false
  return (item.text ?? '').trim().length === 0 && (item.files ?? []).length === 0
})

const title = computed(() => assignmentTitle(props.assignment.title, props.assignment.id))

/* Ostidagi sahifa navbat ochiq paytda skroll qilmasin (eski ilovadagidek). */
let savedBodyOverflow = ''

onMounted(() => {
  savedBodyOverflow = document.body.style.overflow
  document.body.style.overflow = 'hidden'
  // Fokus ildizga: navbat ochilishi bilan `1`–`5` ishlasin, ustoz avval
  // sichqoncha bilan biror joyni bosishi shart bo'lmasin.
  void nextTick(() => rootElement.value?.focus())
})

onBeforeUnmount(() => {
  document.body.style.overflow = savedBodyOverflow
})
</script>

<template>
  <Teleport to="body">
    <!--
      `z-40`: `BaseModal` (kattalashtirilgan rasm) `z-50` da turadi va DOIM
      ustida bo'lishi kerak. Tema `<html>` da, shuning uchun `body` ga
      teleport qilingan bu blok ham ustoz ranglarida qoladi.
    -->
    <div
      ref="rootElement"
      class="fixed inset-0 z-40 flex flex-col bg-ink-950"
      role="dialog"
      aria-modal="true"
      aria-label="Tekshirish navbati"
      tabindex="-1"
    >
      <header
        class="flex shrink-0 items-center gap-3 border-b border-line bg-ink-900 px-2 py-2 sm:px-5"
      >
        <button
          type="button"
          class="tap-target flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
          title="Yopish (Esc)"
          @click="close"
        >
          <AppIcon
            name="close"
            :size="18"
          />
        </button>

        <span class="shrink-0 text-sm font-bold tabular-nums">
          {{ position }} / {{ queue.length }}
        </span>

        <div class="h-1.5 min-w-16 flex-1 overflow-hidden rounded-full bg-ink-750">
          <div
            class="h-full bg-brand-500 transition-[width] duration-300"
            :style="{ width: `${progressPercent}%` }"
          />
        </div>

        <p
          class="hidden max-w-56 shrink-0 truncate text-xs text-slate-400 md:block"
          v-text="title"
        />
      </header>

      <div
        ref="bodyElement"
        class="scrollbar-slim min-h-0 flex-1 overflow-y-auto px-3 py-4 sm:px-5"
      >
        <div class="mx-auto w-full max-w-[840px]">
          <DataStatus
            :pending="submissionsQuery.isPending.value"
            :error="loadError"
            :empty="current === null"
            :retrying="submissionsQuery.isFetching.value"
            :skeleton-rows="3"
            empty-icon="check"
            :empty-title="doneTitle"
            :empty-text="doneText"
            @retry="submissionsQuery.refetch()"
          >
            <template #empty-action>
              <div class="flex flex-wrap justify-center gap-2">
                <BaseButton
                  v-if="queue.length > 0"
                  variant="secondary"
                  @click="restart"
                >
                  Boshidan
                </BaseButton>
                <BaseButton @click="close">
                  Yopish
                </BaseButton>
              </div>
            </template>

            <template v-if="current !== null">
              <h2
                class="text-xl font-extrabold"
                v-text="current.studentName ?? '—'"
              />
              <p class="mt-1 text-[13px] text-slate-400">
                <span
                  class="tabular-nums"
                  v-text="formatDateTime(current.submittedAt)"
                />
                · {{ current.attemptNumber }}-urinish
                <span
                  v-if="current.isLate"
                  class="text-amber-400"
                > · kechikkan</span>
              </p>

              <div class="mt-3.5 rounded-xl border border-line bg-ink-900 p-3.5">
                <p
                  class="text-[15px] font-semibold"
                  v-text="title"
                />
                <p
                  v-if="
                    props.assignment.description !== null
                      && props.assignment.description.length > 0
                  "
                  class="mt-1.5 text-[13px] leading-relaxed text-slate-400"
                  v-text="props.assignment.description"
                />
              </div>

              <!-- Fayllar MATNDAN OLDIN: ovozli va rasmli javob asosiy baholash materiali. -->
              <div
                v-if="(current.files ?? []).length > 0"
                class="mt-4"
              >
                <SubmissionAttachments
                  :files="current.files ?? []"
                  @zoom="(url) => (zoomUrl = url)"
                />
              </div>

              <template v-if="(current.text ?? '').trim().length > 0">
                <h3 class="mb-1.5 mt-4 text-[11px] font-bold uppercase tracking-wide text-slate-400">
                  Matnli javob
                </h3>
                <p
                  class="whitespace-pre-wrap rounded-xl border border-line bg-ink-900 p-3.5 text-sm leading-relaxed text-slate-200"
                  v-text="current.text"
                />
              </template>

              <p
                v-if="answerIsEmpty"
                class="mt-4 text-sm text-dim"
              >
                Javob bo‘sh.
              </p>
            </template>
          </DataStatus>
        </div>
      </div>

      <footer
        v-if="current !== null"
        class="shrink-0 border-t border-line bg-ink-900 px-3 py-3 sm:px-5"
        :style="{ paddingBottom: 'calc(0.75rem + env(safe-area-inset-bottom, 0px))' }"
      >
        <div class="mx-auto w-full max-w-[840px]">
          <!-- Baho tugmalari: raqam AYNAN klaviatura yorlig'i bilan bir xil. -->
          <div class="flex flex-wrap items-center gap-2">
            <BaseButton
              v-for="value in gradeOptions"
              :key="value"
              size="lg"
              :variant="hasScore && parsedScore === value ? 'primary' : 'secondary'"
              @click="pick(value)"
            >
              {{ value }}
            </BaseButton>

            <label class="flex items-center gap-1.5 text-[11px] text-dim">
              <span>boshqa ball</span>
              <input
                v-model="scoreText"
                class="zn-input w-20 text-center tabular-nums"
                inputmode="decimal"
                :placeholder="String(props.assignment.maxScore)"
                aria-label="Ball"
              >
            </label>

            <span class="text-[11px] text-dim">maks {{ props.assignment.maxScore }}</span>
          </div>

          <!--
            Tayyor izohlar — eng ko'p takrorlanadigan iboralar.

            BITTA QATOR va gorizontal skroll: telefonda o'ralganda ular uch
            qator egallab, javob matni uchun joy qolmasdi (390px ekranda
            futer ekranning yarmidan ko'pini yeb qo'yardi).
          -->
          <div class="scroll-x-safe scrollbar-none -mx-3 mt-2.5 px-3 sm:mx-0 sm:px-0">
            <div class="flex w-max gap-1.5">
              <BaseButton
                v-for="text in QUICK_FEEDBACK"
                :key="text"
                size="sm"
                variant="ghost"
                @click="feedback = text"
              >
                {{ text }}
              </BaseButton>
            </div>
          </div>

          <input
            ref="feedbackInput"
            v-model="feedback"
            class="zn-input mt-2.5"
            placeholder="Izoh (ixtiyoriy, o‘quvchiga ko‘rinadi)"
            autocomplete="off"
            aria-label="Izoh"
          >

          <p
            v-if="scoreError !== null"
            class="mt-2 text-xs text-rose-400"
            role="alert"
            v-text="scoreError"
          />
          <p
            v-else-if="formError !== null"
            class="mt-2 text-xs text-rose-400"
            role="alert"
            v-text="formError"
          />
          <p
            v-else-if="savedNotice !== null"
            class="mt-2 text-xs text-green-400"
            role="status"
            v-text="savedNotice"
          />

          <div class="mt-3 flex flex-wrap gap-2">
            <BaseButton
              class="min-w-56 flex-1"
              size="lg"
              :disabled="!hasScore"
              :loading="gradeMutation.isPending.value"
              @click="save"
            >
              Baholash va keyingisi →
            </BaseButton>
            <BaseButton
              size="lg"
              variant="secondary"
              :loading="reopenMutation.isPending.value"
              @click="reopen"
            >
              Qaytarish
            </BaseButton>
            <BaseButton
              size="lg"
              variant="secondary"
              @click="next"
            >
              O‘tkazish
            </BaseButton>
          </div>

          <!--
            Yorliqlar RO'YXATI KO'RINIB TURADI: yangi ustoz ularni boshqa
            hech qayerdan bilmaydi. Eski ilovada ham aynan shu matn, aynan
            shu joyda va faqat keng ekranda ko'rsatilgan (telefonda klaviatura
            yo'q — joy behuda ketardi).
          -->
          <p class="mt-2.5 hidden text-center text-[11px] text-dim min-[820px]:block">
            Klaviatura:
            <b class="text-slate-400">1–{{ gradeOptions.length }}</b> baho ·
            <b class="text-slate-400">Enter</b> saqlash ·
            <b class="text-slate-400">→</b> o‘tkazib yuborish ·
            <b class="text-slate-400">Space</b> audio ·
            <b class="text-slate-400">Esc</b> yopish
          </p>
        </div>
      </footer>
    </div>
  </Teleport>

  <!--
    Kattalashtirilgan rasm. `BaseModal` ishlatiladi (yangi oyna komponenti
    yozilmaydi): u fokusni, `Esc` ni va `body` skrollini o'zi boshqaradi.
  -->
  <BaseModal
    :open="zoomUrl !== null"
    title="Rasm"
    wide
    @close="zoomUrl = null"
  >
    <img
      v-if="zoomUrl !== null"
      :src="zoomUrl"
      alt="O‘quvchi yuborgan rasm"
      class="mx-auto max-h-[75dvh] w-auto rounded-lg"
    >
  </BaseModal>
</template>
