<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import {
  fetchAvailableTests,
  fetchMyTestResult,
  fetchTestForTaking,
  startTest,
  submitTest,
  testBlockedReason,
  testKindLabel,
  testTitle,
} from '@/entities/test'
import TestResultCard from '@/features/test-take/ui/TestResultCard.vue'
import TestRunner from '@/features/test-take/ui/TestRunner.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { MyResultDto, SubmitTestRequest } from '@/shared/types'
import { AppIcon, BaseButton, BaseCard, DataStatus, PageHeader } from '@/shared/ui'

/**
 * Test yechish ekrani (o'quvchi).
 *
 * NEGA MODAL EMAS, ALOHIDA MARSHRUT: test 20+ savoldan iborat bo'ladi va
 * telefonda bir necha ekran egallaydi; modal ichida tasodifiy "tashqariga
 * bosish" belgilangan javoblarni yo'q qilardi. Marshrut, qolaversa, ro'yxatga
 * qaytish va "orqaga" tugmasini tabiiy qiladi.
 *
 * ★ SERVER OQIMI QAT'IY: `POST /start` -> `GET /take` -> `POST /submit`.
 * `take` urinishsiz 409 beradi ("Avval testni boshlang"), shuning uchun
 * varaqa faqat `start` muvaffaqiyatli tugagach so'raladi.
 *
 * `start` ATAYLAB avtomatik chaqirilmaydi: sahifani ochishning o'zi taymerni
 * ishga tushirsa, testni "ko'rib qo'ymoqchi" bo'lgan o'quvchi vaqtini
 * yo'qotardi. Boshlash — oshkora tugma.
 */
const route = useRoute()
const router = useRouter()
const queryClient = useQueryClient()

const rawId = route.params['testId']
const testId = Number(Array.isArray(rawId) ? rawId[0] : rawId)
const isValidId = Number.isInteger(testId) && testId > 0

/*
  Test tavsifi `available` ro'yxatidan olinadi: backendda o'quvchi uchun
  "bitta test" endpointi YO'Q (`GET /tests/{id}` faqat xodim uchun, 403).
  Ro'yxat baribir keshda turadi — sahifaga ro'yxatdan kelinganda qo'shimcha
  so'rov bo'lmaydi.
*/
const testsQuery = useQuery({
  queryKey: ['tests', 'available'],
  queryFn: ({ signal }) => fetchAvailableTests({ signal }),
  enabled: isValidId,
})

const test = computed(
  () => (testsQuery.data.value ?? []).find((item) => item.id === testId) ?? null,
)

const listError = computed(() => {
  if (!isValidId) return 'Test manzili noto‘g‘ri.'
  return testsQuery.error.value !== null ? toUserMessage(testsQuery.error.value) : null
})

const blockedReason = computed(() => (test.value === null ? null : testBlockedReason(test.value)))

/* ------------------------------------------------------------------ natija */

/** Topshirish javobidan kelgan natija — eng ishonchli manba. */
const submittedResult = ref<MyResultDto | null>(null)

/**
 * 409 dan keyin natijani SERVERDAN so'rash kerak bo'ladi: "vaqt tugadi" va
 * "ikki marta yuborildi" holatlarida urinish allaqachon YOPILGAN, ya'ni
 * o'quvchi baribir natijasini ko'rishi kerak.
 */
const resultRequested = ref(false)

const needResult = computed(
  () => isValidId && (resultRequested.value || test.value?.myStatus === 'Submitted'),
)

const myResultQuery = useQuery({
  queryKey: ['tests', testId, 'my-result'],
  queryFn: ({ signal }) => fetchMyTestResult(testId, { signal }),
  enabled: needResult,
  // Urinish yopilgach natija o'zgarmaydi — qayta so'rashning ma'nosi yo'q.
  staleTime: Infinity,
})

const result = computed<MyResultDto | null>(() => {
  const fresh = submittedResult.value
  if (fresh !== null) return fresh
  const loaded = myResultQuery.data.value ?? null
  // `InProgress` natija emas: urinish boshlangan, lekin topshirilmagan.
  return loaded !== null && loaded.status === 'Submitted' ? loaded : null
})

/* ---------------------------------------------------------------- boshlash */

const started = ref(false)
const startError = ref<string | null>(null)

const startMutation = useMutation({
  mutationFn: () => startTest(testId),
  onSuccess: () => {
    started.value = true
  },
  onError: (error: Error) => {
    /*
      409 — test e'lon qilinmagan / muddati tugagan / allaqachon topshirilgan;
      403 — dars qulflangan yoki profil faol emas (server matni ko'rsatiladi).
      Ikkalasini ham `toUserMessage` o'qiydi.
    */
    startError.value = toUserMessage(error)
    // "Allaqachon topshirgansiz" holatida natijani ko'rsatib qo'yamiz.
    resultRequested.value = true
  },
})

const takeQuery = useQuery({
  queryKey: ['tests', testId, 'take'],
  queryFn: ({ signal }) => fetchTestForTaking(testId, { signal }),
  enabled: computed(() => started.value && result.value === null),
  // Varaqa keshdan olinmaydi: `deadline` vaqtga bog'liq qiymat.
  staleTime: 0,
  gcTime: 0,
})

const takeError = computed(() =>
  takeQuery.error.value !== null ? toUserMessage(takeQuery.error.value) : null,
)

/* -------------------------------------------------------------- topshirish */

const submitError = ref<string | null>(null)

const submitMutation = useMutation({
  mutationFn: (payload: SubmitTestRequest) => submitTest(testId, payload),
  onSuccess: (data) => {
    submittedResult.value = data
    submitError.value = null
    invalidateAfterAttempt()
  },
  onError: (error: Error) => {
    /*
      ★ 409 NING IKKI SABABI VA IKKALASI HAM YAKUNIY:
        • "Test uchun ajratilgan vaqt tugagan — urinish yopildi" (server
          urinishni 0 ball bilan yopdi);
        • "Bu test ayni damda topshirildi (ikki marta yuborilgan)" — `xmin`
          qulfi yoki unikal indeks ishga tushdi.
      Ikkalasida ham urinish YOPIQ, shuning uchun natija so'raladi va
      server matni o'zgartirilmasdan ko'rsatiladi.
      403 — dars qulflandi yoki profil o'chirildi.
    */
    submitError.value = toUserMessage(error)
    resultRequested.value = true
    invalidateAfterAttempt()
  },
})

/**
 * Urinish yopilgach kesh eskiradi.
 *
 * KALITLAR ANIQ SANALADI, `['tests']` prefiksi BILAN EMAS: keng invalidatsiya
 * `['tests', id, 'take']` ni ham eskirtirardi va 409 dan keyin varaqa uchun
 * yana bir bekorga so'rov ketardi (u baribir 409 qaytaradi).
 *
 * `assignments/mine` ham yangilanadi: dars testi topshirilgach server GATING
 * keshini bekor qiladi (`TestService` -> `gating.InvalidateAsync`), ya'ni
 * keyingi dars ochilib yangi vazifa paydo bo'lishi mumkin.
 */
function invalidateAfterAttempt(): void {
  void queryClient.invalidateQueries({ queryKey: ['tests', 'available'] })
  void queryClient.invalidateQueries({ queryKey: ['tests', testId, 'my-result'] })
  void queryClient.invalidateQueries({ queryKey: ['assignments', 'mine'] })
}

const confirm = useConfirm()

/**
 * R4 — TESTNI BOSHLASH TASDIQLANADI, `warning` TONIDA.
 *
 * ★ NEGA KERAK: bir bosish SERVERDA taymerni ishga tushiradi va uni
 * to'xtatib bo'lmaydi (sahifani yopish ham yordam bermaydi). Urinish
 * BITTA — ya'ni tasodifiy bosish o'quvchining yagona imkoniyatini
 * sarflaydi. Qoidalar sahifada allaqachon yozilgan, lekin ular
 * O'QILMASDAN o'tib ketiladigan matn; tasdiq oynasi ularni bosish
 * YO'LIGA qo'yadi.
 *
 * ★ "DAVOM ETTIRISH" DA TASDIQ SO'RALMAYDI: urinish ALLAQACHON ochiq va
 * taymer ketyapti, ya'ni ogohlantiradigan yangi oqibat yo'q. Aksincha,
 * bu yerdagi har qo'shimcha qadam vaqti sanalayotgan o'quvchining
 * sekundlarini yeydi.
 */
async function handleStart(): Promise<void> {
  if (startMutation.isPending.value) return

  const current = test.value
  if (current !== null && current.myStatus !== 'InProgress') {
    const details = ['Bitta testga bitta urinish beriladi — topshirilgach o‘zgartirib bo‘lmaydi.']
    if (current.timeLimitMinutes !== null) {
      details.push(
        `Vaqt SERVERDA sanaladi: ${current.timeLimitMinutes} daqiqadan keyin urinish avtomatik yopiladi.`,
      )
      details.push('Sahifani yangilash yoki tabni yopish taymerni to‘xtatmaydi.')
    }

    const ok = await confirm({
      title: 'Testni boshlash',
      message: `“${testTitle(current)}” boshlanadi va vaqt shu ondan sanala boshlaydi.`,
      confirmLabel: 'Boshlash',
      tone: 'warning',
      details,
    })
    if (!ok) return
  }

  startError.value = null
  startMutation.mutate()
}

function handleSubmit(payload: SubmitTestRequest): void {
  if (submitMutation.isPending.value) return
  submitError.value = null
  submitMutation.mutate(payload)
}

function goBack(): void {
  void router.push({ name: 'student-tests' })
}
</script>

<template>
  <div>
    <button
      type="button"
      class="mb-3 inline-flex min-h-11 items-center gap-1.5 rounded-lg pr-3 text-xs font-medium text-slate-400 transition-colors hover:text-slate-100"
      @click="goBack"
    >
      <AppIcon
        name="arrow-left"
        :size="14"
      />
      Testlarim
    </button>

    <DataStatus
      :pending="testsQuery.isPending.value && isValidId"
      :error="listError"
      :empty="test === null && !testsQuery.isPending.value && listError === null"
      :retrying="testsQuery.isFetching.value"
      :skeleton-rows="3"
      empty-icon="award"
      empty-title="Test topilmadi"
      empty-text="Bu test sizga ochiq emas yoki e’lon qilinmagan."
      @retry="testsQuery.refetch()"
    >
      <template v-if="test !== null">
        <!-- ================================================= NATIJA -->
        <template v-if="result !== null">
          <PageHeader
            title="Test natijasi"
            :subtitle="testKindLabel(test.kind)"
          />

          <p
            v-if="submitError !== null"
            class="mb-3 rounded-lg border border-amber-500/25 bg-amber-500/10 px-3.5 py-3 text-xs leading-relaxed text-amber-200"
            role="alert"
            v-text="submitError"
          />

          <TestResultCard :result="result" />

          <div class="mt-4">
            <BaseButton
              variant="secondary"
              @click="goBack"
            >
              Testlar ro‘yxatiga qaytish
            </BaseButton>
          </div>
        </template>

        <!-- ================================================= YECHISH -->
        <template v-else-if="started">
          <DataStatus
            :pending="takeQuery.isPending.value"
            :error="takeError"
            :empty="(takeQuery.data.value?.questions ?? []).length === 0 && !takeQuery.isPending.value && takeError === null"
            :retrying="takeQuery.isFetching.value"
            :skeleton-rows="4"
            empty-icon="file-text"
            empty-title="Savol yo‘q"
            empty-text="Bu testga hali savol qo‘shilmagan."
            @retry="takeQuery.refetch()"
          >
            <TestRunner
              v-if="takeQuery.data.value !== undefined"
              :take="takeQuery.data.value"
              :pending="submitMutation.isPending.value"
              :error-message="submitError"
              @submit="handleSubmit"
            />
          </DataStatus>
        </template>

        <!-- ================================================= KIRISH EKRANI -->
        <template v-else>
          <PageHeader
            :title="testTitle(test)"
            :subtitle="testKindLabel(test.kind)"
          />

          <BaseCard>
            <p
              v-if="test.description !== null && test.description.length > 0"
              class="whitespace-pre-line text-sm text-slate-300"
              v-text="test.description"
            />

            <dl class="mt-3 flex flex-wrap gap-x-5 gap-y-2 text-xs text-slate-400">
              <div class="inline-flex items-center gap-1.5">
                <AppIcon
                  name="file-text"
                  :size="13"
                />
                <span class="tabular-nums">{{ test.questionCount }} savol</span>
              </div>
              <div class="inline-flex items-center gap-1.5">
                <AppIcon
                  name="star"
                  :size="13"
                />
                <span class="tabular-nums">{{ test.maxScore }} ball</span>
              </div>
              <div
                v-if="test.timeLimitMinutes !== null"
                class="inline-flex items-center gap-1.5"
              >
                <AppIcon
                  name="clock"
                  :size="13"
                />
                <span class="tabular-nums">{{ test.timeLimitMinutes }} daqiqa</span>
              </div>
              <div
                v-if="test.dueAt !== null"
                class="inline-flex items-center gap-1.5"
              >
                <AppIcon
                  name="calendar"
                  :size="13"
                />
                <span
                  class="tabular-nums"
                  v-text="formatDateTime(test.dueAt)"
                />
              </div>
              <div
                v-if="test.moduleLessonName !== null"
                class="inline-flex min-w-0 items-center gap-1.5"
              >
                <AppIcon
                  name="grid"
                  :size="13"
                />
                <span
                  class="truncate"
                  v-text="test.moduleLessonName"
                />
              </div>
            </dl>

            <!--
              QOIDALAR OLDINDAN aytiladi: o'quvchi taymer ishga tushgandan
              keyin emas, BOSHLASHDAN OLDIN bilishi kerak.
            -->
            <ul class="mt-4 space-y-1.5 text-xs leading-relaxed text-slate-400">
              <li class="flex items-start gap-2">
                <AppIcon
                  name="alert"
                  :size="13"
                  class="mt-0.5 shrink-0"
                />
                <span>Bitta testga bitta urinish beriladi — topshirilgach o‘zgartirib bo‘lmaydi.</span>
              </li>
              <li
                v-if="test.timeLimitMinutes !== null"
                class="flex items-start gap-2"
              >
                <AppIcon
                  name="clock"
                  :size="13"
                  class="mt-0.5 shrink-0"
                />
                <span>
                  Vaqt SERVERDA hisoblanadi va {{ test.timeLimitMinutes }} daqiqadan keyin
                  urinish avtomatik yopiladi. Sahifani yangilash yoki tabni yopish vaqtni
                  to‘xtatmaydi.
                </span>
              </li>
              <li class="flex items-start gap-2">
                <AppIcon
                  name="check"
                  :size="13"
                  class="mt-0.5 shrink-0"
                />
                <span>
                  Ba’zi savollarda bir nechta to‘g‘ri javob bo‘ladi — ular alohida
                  belgilanadi va barcha to‘g‘ri variant tanlanishi kerak (qisman ball yo‘q).
                </span>
              </li>
            </ul>

            <p
              v-if="blockedReason !== null"
              class="mt-4 flex items-start gap-2 rounded-lg bg-ink-800 px-3 py-2 text-xs text-slate-300"
            >
              <AppIcon
                name="lock"
                :size="14"
                class="mt-px"
              />
              <span v-text="blockedReason" />
            </p>

            <p
              v-if="startError !== null"
              class="mt-4 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3.5 py-3 text-xs leading-relaxed text-rose-200"
              role="alert"
              v-text="startError"
            />

            <div class="mt-4 flex justify-end">
              <BaseButton
                :disabled="blockedReason !== null"
                :loading="startMutation.isPending.value"
                @click="handleStart"
              >
                <template #icon>
                  <AppIcon
                    name="play"
                    :size="15"
                  />
                </template>
                {{ test.myStatus === 'InProgress' ? 'Davom ettirish' : 'Testni boshlash' }}
              </BaseButton>
            </div>
          </BaseCard>
        </template>
      </template>
    </DataStatus>
  </div>
</template>
