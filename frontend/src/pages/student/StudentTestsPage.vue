<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import {
  fetchAvailableTests,
  scoreLabel,
  testBlockedReason,
  testKindLabel,
  testStatusLabel,
  testStatusTone,
  testTitle,
} from '@/entities/test'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import { AppIcon, BaseBadge, BaseButton, DataStatus } from '@/shared/ui'
import StudentSubHeader from '@/widgets/student-shell/ui/StudentSubHeader.vue'

/**
 * O'quvchi testlari.
 *
 * Sahifa YUPQA: yechish oqimi (`start` -> `take` -> `submit`) alohida
 * marshrutda (`student-test-take`) va `features/test-take/` da yashaydi.
 *
 * TOPSHIRILGAN test ro'yxatdan CHIQIB KETMAYDI — o'quvchi natijasini
 * ko'rishi kerak. Boshlab bo'lmasligining SABABI har kartochkada matn
 * bilan yoziladi (`testBlockedReason`), aks holda "nega tugma yo'q?" degan
 * savol qo'llab-quvvatlashga tushardi.
 */
const router = useRouter()

const testsQuery = useQuery({
  queryKey: ['tests', 'available'],
  queryFn: ({ signal }) => fetchAvailableTests({ signal }),
})

const rows = computed(() =>
  (testsQuery.data.value ?? []).map((test) => ({
    test,
    label: testStatusLabel(test),
    tone: testStatusTone(test),
    blockedReason: testBlockedReason(test),
  })),
)

const errorMessage = computed(() =>
  testsQuery.error.value !== null ? toUserMessage(testsQuery.error.value) : null,
)

function open(testId: number): void {
  void router.push({ name: 'student-test-take', params: { testId: String(testId) } })
}
</script>

<template>
  <div>
    <!-- Sahifa "O'quv" tabining ichida — orqaga qaytish yo'li ko'rinib tursin. -->
    <StudentSubHeader
      title="Testlarim"
      subtitle="Ochiq testlar va natijalaringiz"
    />

    <DataStatus
      :pending="testsQuery.isPending.value"
      :error="errorMessage"
      :empty="rows.length === 0"
      :retrying="testsQuery.isFetching.value"
      empty-icon="award"
      empty-title="Test yo‘q"
      empty-text="Ochiq test paydo bo‘lganda shu yerda ko‘rinadi."
      @retry="testsQuery.refetch()"
    >
      <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        <article
          v-for="row in rows"
          :key="row.test.id"
          class="flex flex-col rounded-xl border border-line bg-ink-900 p-3.5 sm:p-4"
        >
          <div class="flex items-start justify-between gap-2">
            <h3
              class="min-w-0 flex-1 text-sm font-semibold text-slate-100"
              v-text="testTitle(row.test)"
            />
            <BaseBadge :tone="row.tone">
              {{ row.label }}
            </BaseBadge>
          </div>

          <p
            class="mt-1 text-xs text-dim"
            v-text="testKindLabel(row.test.kind)"
          />

          <dl class="mt-3 flex flex-wrap gap-x-4 gap-y-1.5 text-xs text-slate-400">
            <div class="inline-flex items-center gap-1.5">
              <AppIcon
                name="file-text"
                :size="13"
              />
              <span class="tabular-nums">{{ row.test.questionCount }} savol</span>
            </div>
            <div class="inline-flex items-center gap-1.5">
              <AppIcon
                name="star"
                :size="13"
              />
              <span class="tabular-nums">{{ row.test.maxScore }} ball</span>
            </div>
            <div
              v-if="row.test.timeLimitMinutes !== null"
              class="inline-flex items-center gap-1.5"
            >
              <AppIcon
                name="clock"
                :size="13"
              />
              <span class="tabular-nums">{{ row.test.timeLimitMinutes }} daqiqa</span>
            </div>
            <div
              v-if="row.test.dueAt !== null"
              class="inline-flex items-center gap-1.5"
            >
              <AppIcon
                name="calendar"
                :size="13"
              />
              <span
                class="tabular-nums"
                v-text="formatDateTime(row.test.dueAt)"
              />
            </div>
          </dl>

          <p
            v-if="row.test.myScore !== null"
            class="mt-3 rounded-lg bg-ink-800 px-3 py-2 text-xs text-slate-200"
          >
            Natijangiz:
            <span
              class="font-semibold tabular-nums text-green-400"
              v-text="scoreLabel(row.test.myScore, row.test.maxScore)"
            />
          </p>

          <!-- Nega boshlab bo'lmasligi — kartochkadagi eng muhim ma'lumot. -->
          <p
            v-else-if="row.blockedReason !== null"
            class="mt-3 flex items-start gap-2 rounded-lg bg-ink-800 px-3 py-2 text-xs text-slate-300"
          >
            <AppIcon
              name="lock"
              :size="13"
              class="mt-px"
            />
            <span v-text="row.blockedReason" />
          </p>

          <!--
            Tugma HAR DOIM bor: topshirgan o'quvchi ham kirishi kerak —
            natija ekrani aynan shu marshrutda ko'rsatiladi.
          -->
          <div class="mt-auto flex justify-end pt-3">
            <BaseButton
              size="sm"
              :variant="row.test.canStart ? 'primary' : 'secondary'"
              @click="open(row.test.id)"
            >
              <template #icon>
                <AppIcon
                  :name="row.test.canStart ? 'play' : 'award'"
                  :size="13"
                />
              </template>
              {{
                row.test.myStatus === 'Submitted'
                  ? 'Natijani ko‘rish'
                  : row.test.myStatus === 'InProgress'
                    ? 'Davom ettirish'
                    : 'Boshlash'
              }}
            </BaseButton>
          </div>
        </article>
      </div>
    </DataStatus>
  </div>
</template>
