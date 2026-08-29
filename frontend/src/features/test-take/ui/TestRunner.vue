<script setup lang="ts">
import { computed, ref, shallowRef, watch } from 'vue'

import { testTitle } from '@/entities/test'
import { formatDateTime } from '@/shared/lib/datetime'
import type { SubmitTestRequest, TakeQuestionDto, TakeTestDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseModal } from '@/shared/ui'

import { answeredCount, isSelected, toggleOption, toSubmitRequest } from '../model/answers'
import type { AnswerMap } from '../model/answers'
import { useAttemptCountdown } from '../model/useAttemptCountdown'

/**
 * Test yechish varaqasi.
 *
 * ★ IKKI QOIDA BU YERDA KO'RINADIGAN QILINGAN:
 *
 *  1) VAQT CHEGARASI SERVERDA. Taymer `take.deadline` (server hisoblagan on)
 *     bo'yicha sanaydi va nolga yetganda "topshirildi" deb YOZMAYDI — u
 *     javoblarni SERVERGA yuboradi va qarorni server aytadi. Server muddat
 *     o'tgan bo'lsa urinishni 0 ball bilan yopib 409 qaytaradi, ulgurgan
 *     bo'lsa esa oddiy natija beradi. Klient soati noto'g'ri bo'lsa ham
 *     tizim buzilmaydi.
 *
 *  2) KO'P TO'G'RI JAVOBLI SAVOL. `multipleAnswers` bo'lsa checkbox
 *     ko'rsatiladi, savol boshida "Bir nechta javob" nishoni va "hammasini
 *     belgilang" ogohlantirishi turadi. Baholash "hammasi yoki hech nima"
 *     (`TestQuestion.Score`) — qisman ball YO'Q, shuning uchun bu qoida
 *     o'quvchidan yashirilmaydi.
 */
const props = defineProps<{
  take: TakeTestDto
  /** Topshirish so'rovi ketyaptimi — takroriy bosish to'siladi. */
  pending: boolean
  /** Server xatosi (409/403) — `toUserMessage` natijasi. */
  errorMessage: string | null
}>()

const emit = defineEmits<{ submit: [payload: SubmitTestRequest] }>()

const questions = computed<TakeQuestionDto[]>(() => props.take.questions ?? [])

// `shallowRef`: xarita HAR DOIM butunligicha almashtiriladi (`toggleOption`
// yangi obyekt qaytaradi), ya'ni chuqur reaktivlik ortiqcha xarajat bo'lardi.
const answers = shallowRef<AnswerMap>({})

// Boshqa testga o'tilsa (yoki varaqa qayta yuklansa) tanlovlar tozalanadi —
// aks holda eski savol ID'lari yangi testga "yopishib" qolardi.
watch(
  () => props.take.attemptId,
  () => {
    answers.value = {}
    autoSubmitted.value = false
    confirmOpen.value = false
  },
)

/*
  `computed` ishlatiladi, `toRef(props.take, 'deadline')` EMAS: `toRef` obyekt
  nusxasiga bog'lanadi va `take` yangi obyekt bilan almashtirilsa (so'rov qayta
  bajarilganda) eski muddatda qotib qolardi.
*/
const countdown = useAttemptCountdown(computed(() => props.take.deadline))

const answered = computed(() => answeredCount(answers.value, questions.value))
const unanswered = computed(() => questions.value.length - answered.value)

function pick(question: TakeQuestionDto, optionId: number): void {
  if (props.pending) return
  answers.value = toggleOption(answers.value, question, optionId)
}

function checked(question: TakeQuestionDto, optionId: number): boolean {
  return isSelected(answers.value, question.id, optionId)
}

/* ------------------------------------------------------------- topshirish */

const confirmOpen = ref(false)

/** Avtomatik topshirish BIR MARTA bo'ladi — taymer har soniyada tiklanmasin. */
const autoSubmitted = ref(false)

/**
 * ★ SOAT FARQIDAN HIMOYA.
 *
 * Taymer BRAUZER soatiga tayanadi, u esa noto'g'ri sozlangan bo'lishi mumkin
 * (telefonlarda odatiy hol). Agar qurilma soati serverdan oldinda bo'lsa,
 * varaqa ochilishi bilanoq "vaqt tugadi" deb hisoblanib, avtomatik topshirish
 * o'quvchining urinishini BO'SH javob bilan yopib qo'yardi — server esa
 * aslida hali qabul qilayotgan bo'lardi.
 *
 * Shuning uchun avtomatik topshirish faqat sanoq HAQIQATAN kuzatilgan
 * bo'lsa ishlaydi: bir marta "qolgan vaqt > 0" holatini ko'rgan bo'lishimiz
 * shart. Soat noto'g'ri bo'lsa taymer 00:00 ko'rsatadi, lekin javoblar
 * o'z-o'zidan yuborilmaydi — o'quvchi tugmani o'zi bosadi va qarorni
 * server aytadi.
 *
 * Muddat allaqachon o'tgan holatni server MUSTAQIL ushlaydi: `GET /take`
 * `EnsureWithinTimeLimitAsync` orqali o'tadi va urinishni 0 ball bilan yopib
 * 409 qaytaradi — bunda bu komponent umuman chizilmaydi.
 */
const sawRunningTimer = ref(false)

watch(countdown.remainingMs, (remaining) => {
  if (remaining !== null && remaining > 0) sawRunningTimer.value = true
})

function send(): void {
  emit('submit', toSubmitRequest(answers.value, questions.value))
}

watch(countdown.expired, (isExpired) => {
  if (!isExpired || autoSubmitted.value || !sawRunningTimer.value) return
  autoSubmitted.value = true
  confirmOpen.value = false
  send()
})

function askSubmit(): void {
  if (props.pending) return
  confirmOpen.value = true
}

function confirmSubmit(): void {
  confirmOpen.value = false
  send()
}
</script>

<template>
  <div>
    <!--
      Yopishqoq panel: uzun testda taymer va jarayon ekrandan chiqib
      ketmasligi kerak — telefonda savollar ro'yxati bir necha ekran bo'ladi.
    -->
    <div class="sticky top-0 z-10 -mx-3 mb-4 border-b border-line bg-ink-950/95 px-3 py-2.5 backdrop-blur sm:-mx-5 sm:px-5">
      <div class="flex flex-wrap items-center justify-between gap-x-3 gap-y-1.5">
        <div class="min-w-0">
          <p
            class="truncate text-sm font-semibold text-slate-100"
            v-text="testTitle(props.take)"
          />
          <p class="text-[11px] tabular-nums text-dim">
            {{ answered }} / {{ questions.length }} savolga javob belgilandi
          </p>
        </div>

        <div
          v-if="countdown.label.value !== null"
          class="flex shrink-0 items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-sm font-semibold tabular-nums"
          :class="
            countdown.urgent.value
              ? 'bg-rose-500/15 text-rose-300'
              : 'bg-ink-800 text-slate-200'
          "
          role="timer"
          aria-live="off"
        >
          <AppIcon
            name="clock"
            :size="15"
          />
          {{ countdown.label.value }}
        </div>
      </div>

      <!-- Jarayon chizig'i: raqamdan ko'ra tezroq o'qiladi.
           2026-08-29: `bg-brand-vivid` — yorqin yashil, `StudentLearnPage`
           dagi kurs chizig'i bilan AYNI qoida (izoh o'sha yerda). -->
      <div class="mt-2 h-1 overflow-hidden rounded-full bg-ink-800">
        <div
          class="h-full rounded-full bg-brand-vivid transition-[width] duration-200"
          :style="{ width: `${questions.length === 0 ? 0 : (answered / questions.length) * 100}%` }"
        />
      </div>
    </div>

    <p
      v-if="countdown.label.value !== null"
      class="mb-3 text-[11px] leading-relaxed text-dim"
    >
      Vaqt SERVERDA hisoblanadi. Taymer tugaganda javoblaringiz avtomatik
      yuboriladi — sahifani yangilash yoki tabni yopish vaqtni to‘xtatmaydi.
    </p>

    <p
      v-if="props.take.dueAt !== null"
      class="mb-3 text-[11px] tabular-nums text-dim"
    >
      Topshirish muddati: {{ formatDateTime(props.take.dueAt) }}
    </p>

    <!-- Vaqt tugadi: o'quvchi nima bo'layotganini bilishi kerak. -->
    <p
      v-if="countdown.expired.value"
      class="mb-3 flex items-start gap-2 rounded-lg border border-amber-500/25 bg-amber-500/10 px-3 py-2 text-xs text-amber-200"
      role="status"
    >
      <AppIcon
        name="clock"
        :size="14"
        class="mt-px"
      />
      <span v-if="sawRunningTimer">
        Vaqt tugadi — javoblaringiz serverga yuborilmoqda. Natijani server aytadi.
      </span>
      <!--
        Taymer 00:00 da ochilgan: bu odatda qurilma soati noto'g'ri sozlangani
        bildiradi. Javoblarni O'ZIMIZ yubormaymiz (yuqoridagi izoh) —
        o'quvchining o'zi bosadi va haqiqiy qarorni server beradi.
      -->
      <span v-else>
        Qurilmangiz soati bo‘yicha vaqt tugagan ko‘rinadi. Javoblaringizni
        “Topshirish” tugmasi bilan yuboring — qabul qilinadimi yoki yo‘qmi,
        buni server hal qiladi.
      </span>
    </p>

    <ol class="space-y-3">
      <li
        v-for="(question, index) in questions"
        :key="question.id"
        class="rounded-xl border border-line bg-ink-900 p-3.5 sm:p-4"
      >
        <div class="flex flex-wrap items-start justify-between gap-2">
          <p class="min-w-0 flex-1 text-sm text-slate-100">
            <span class="mr-1.5 font-semibold tabular-nums text-dim">{{ index + 1 }}.</span>
            <span v-text="question.body" />
          </p>
          <div class="flex shrink-0 flex-wrap items-center gap-1.5">
            <BaseBadge
              v-if="question.multipleAnswers"
              tone="assistant"
            >
              Bir nechta javob
            </BaseBadge>
            <BaseBadge tone="neutral">
              {{ question.points }} ball
            </BaseBadge>
          </div>
        </div>

        <!--
          Ko'p javobli savolda BAHOLASH QOIDASI ochiq aytiladi: server
          "hammasi yoki hech nima" bo'yicha baholaydi va qisman ball
          bermaydi — buni yashirish o'quvchini adashtirardi.
        -->
        <p
          v-if="question.multipleAnswers"
          class="mt-1.5 text-[11px] leading-relaxed text-sky-300/80"
        >
          Barcha to‘g‘ri variantlarni belgilang: qisman ball berilmaydi.
        </p>

        <div class="mt-2.5 space-y-1.5">
          <label
            v-for="option in question.options ?? []"
            :key="option.id"
            class="flex min-h-11 cursor-pointer items-center gap-2.5 rounded-lg border px-3 py-2 text-sm transition-colors"
            :class="
              checked(question, option.id)
                ? 'border-brand-500 bg-brand-500/12 text-slate-100'
                : 'border-line bg-ink-950 text-slate-300 hover:bg-ink-850'
            "
          >
            <input
              :type="question.multipleAnswers ? 'checkbox' : 'radio'"
              :name="`question-${question.id}`"
              class="size-4 shrink-0 accent-brand-500"
              :checked="checked(question, option.id)"
              :disabled="props.pending"
              @change="pick(question, option.id)"
            >
            <span
              class="min-w-0"
              v-text="option.body"
            />
          </label>
        </div>
      </li>
    </ol>

    <p
      v-if="props.errorMessage !== null"
      class="mt-4 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3.5 py-3 text-xs leading-relaxed text-rose-200"
      role="alert"
      v-text="props.errorMessage"
    />

    <div class="mt-4 flex flex-col items-stretch gap-2 sm:flex-row sm:items-center sm:justify-end">
      <p
        v-if="unanswered > 0"
        class="text-xs text-amber-300 sm:mr-auto"
      >
        {{ unanswered }} ta savol javobsiz — javobsiz savol 0 ball beradi.
      </p>
      <BaseButton
        :loading="props.pending"
        :disabled="props.pending"
        block
        class="sm:w-auto"
        @click="askSubmit"
      >
        <template #icon>
          <AppIcon
            name="send"
            :size="15"
          />
        </template>
        Topshirish
      </BaseButton>
    </div>

    <BaseModal
      :open="confirmOpen"
      title="Testni topshirasizmi?"
      @close="confirmOpen = false"
    >
      <p class="text-sm text-slate-300">
        Topshirilgandan keyin javoblarni o‘zgartirib bo‘lmaydi: bitta testga
        bitta urinish beriladi.
      </p>
      <p
        v-if="unanswered > 0"
        class="mt-3 rounded-lg border border-amber-500/25 bg-amber-500/10 px-3 py-2 text-xs text-amber-200"
      >
        {{ unanswered }} ta savol javobsiz qolmoqda — ular 0 ball bilan hisoblanadi.
      </p>

      <template #footer>
        <BaseButton
          variant="secondary"
          @click="confirmOpen = false"
        >
          Orqaga
        </BaseButton>
        <BaseButton
          :loading="props.pending"
          @click="confirmSubmit"
        >
          Ha, topshiraman
        </BaseButton>
      </template>
    </BaseModal>
  </div>
</template>
