<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import {
  deleteTest,
  fetchTestForAuthoring,
  publishBlockedReason,
  publishTest,
  testKindLabel,
  testStructureLocked,
  testTitle,
  unpublishTest,
} from '@/entities/test'
import TestFormDialog from '@/features/test-form/ui/TestFormDialog.vue'
import TestQuestionsEditor from '@/features/test-questions/ui/TestQuestionsEditor.vue'
import TestResultsPanel from '@/features/test-results/ui/TestResultsPanel.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  ConfirmDeleteDialog,
  DataStatus,
  PageHeader,
} from '@/shared/ui'

/**
 * Bitta testning sahifasi: tavsif, savollar va natijalar (Academic/Admin).
 *
 * SAHIFA YUPQA — savol CRUD `features/test-questions/`, natijalar va CSV
 * eksport `features/test-results/`, forma esa `features/test-form/` da.
 * Bu yerda faqat sahifa holati va e'lon qilish/o'chirish amallari bor.
 */
const route = useRoute()
const router = useRouter()
const queryClient = useQueryClient()

const rawId = route.params['testId']
const testId = Number(Array.isArray(rawId) ? rawId[0] : rawId)
const isValidId = Number.isInteger(testId) && testId > 0

const testQuery = useQuery({
  queryKey: ['tests', testId, 'authoring'],
  queryFn: ({ signal }) => fetchTestForAuthoring(testId, { signal }),
  enabled: isValidId,
})

const test = computed(() => testQuery.data.value?.test ?? null)
const questions = computed(() => testQuery.data.value?.questions ?? [])

const errorMessage = computed(() => {
  if (!isValidId) return 'Test manzili noto‘g‘ri.'
  return testQuery.error.value !== null ? toUserMessage(testQuery.error.value) : null
})

/** Savollarni o'zgartirish mumkinmi (urinishlar bo'lsa server 409 beradi). */
const locked = computed(() => (test.value === null ? false : testStructureLocked(test.value)))

const publishBlocked = computed(() =>
  test.value === null ? null : publishBlockedReason(test.value),
)

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['tests'] })
}

/* ------------------------------------------------------- e'lon qilish */

const actionError = ref<string | null>(null)

const publishMutation = useMutation({
  mutationFn: (publish: boolean) => (publish ? publishTest(testId) : unpublishTest(testId)),
  onSuccess: () => {
    actionError.value = null
    refresh()
  },
  onError: (error: Error) => {
    /*
      409 — `Test.Publish()` rad etdi: bo'sh test yoki nuqsonli savol
      (kamida 2 variant, kamida 1 to'g'ri). Sabab `detail` da to'liq keladi
      va O'Z SO'ZIMIZ bilan qayta yozilmaydi — aks holda xodim qaysi savol
      nuqsonli ekanini bilmasdi.
    */
    actionError.value = toUserMessage(error)
  },
})

/* ---------------------------------------------------------- tahrirlash */

const editOpen = ref(false)

/* ----------------------------------------------------------- o'chirish */

const deleteOpen = ref(false)
const deleteError = ref<string | null>(null)

const deleteMutation = useMutation({
  mutationFn: () => deleteTest(testId),
  onSuccess: () => {
    deleteOpen.value = false
    refresh()
    void router.push({ name: 'manage-tests' })
  },
  onError: (error: Error) => {
    // 409 — urinish boshlangan: natijalar yo'qolmasligi uchun server rad etadi.
    deleteError.value = toUserMessage(error)
  },
})

function askDelete(): void {
  deleteError.value = null
  deleteOpen.value = true
}
</script>

<template>
  <div>
    <button
      type="button"
      class="mb-3 inline-flex min-h-11 items-center gap-1.5 rounded-lg pr-3 text-xs font-medium text-slate-400 transition-colors hover:text-slate-100"
      @click="router.push({ name: 'manage-tests' })"
    >
      <AppIcon
        name="arrow-left"
        :size="14"
      />
      Testlar
    </button>

    <DataStatus
      :pending="testQuery.isPending.value && isValidId"
      :error="errorMessage"
      :empty="false"
      :retrying="testQuery.isFetching.value"
      :skeleton-rows="3"
      @retry="testQuery.refetch()"
    >
      <template v-if="test !== null">
        <PageHeader
          :title="testTitle(test)"
          :subtitle="`${testKindLabel(test.kind)}${test.moduleLessonName !== null ? ` · ${test.moduleLessonName}` : ''}`"
        >
          <template #actions>
            <BaseBadge :tone="test.isPublished ? 'success' : 'neutral'">
              {{ test.isPublished ? 'E’lon qilingan' : 'Qoralama' }}
            </BaseBadge>

            <BaseButton
              size="sm"
              variant="secondary"
              @click="editOpen = true"
            >
              <template #icon>
                <AppIcon
                  name="edit"
                  :size="13"
                />
              </template>
              Tahrirlash
            </BaseButton>

            <BaseButton
              size="sm"
              :variant="test.isPublished ? 'secondary' : 'success'"
              :disabled="!test.isPublished && publishBlocked !== null"
              :loading="publishMutation.isPending.value"
              @click="publishMutation.mutate(!test.isPublished)"
            >
              <template #icon>
                <AppIcon
                  :name="test.isPublished ? 'eye-off' : 'eye'"
                  :size="13"
                />
              </template>
              {{ test.isPublished ? 'E’lonni qaytarish' : 'E’lon qilish' }}
            </BaseButton>

            <BaseButton
              size="sm"
              variant="danger"
              @click="askDelete"
            >
              <template #icon>
                <AppIcon
                  name="trash"
                  :size="13"
                />
              </template>
              O‘chirish
            </BaseButton>
          </template>
        </PageHeader>

        <p
          v-if="actionError !== null"
          class="mb-4 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3.5 py-3 text-xs leading-relaxed text-rose-200"
          role="alert"
          v-text="actionError"
        />

        <BaseCard class="mb-4">
          <p
            v-if="(test.description ?? '').length > 0"
            class="whitespace-pre-line text-sm text-slate-300"
            v-text="test.description"
          />

          <dl class="mt-2 flex flex-wrap gap-x-5 gap-y-2 text-xs text-slate-400">
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
            <div class="inline-flex items-center gap-1.5">
              <AppIcon
                name="clock"
                :size="13"
              />
              <span class="tabular-nums">
                {{ test.timeLimitMinutes === null ? 'Vaqt chegarasiz' : `${test.timeLimitMinutes} daqiqa` }}
              </span>
            </div>
            <div class="inline-flex items-center gap-1.5">
              <AppIcon
                name="calendar"
                :size="13"
              />
              <span class="tabular-nums">
                {{ test.dueAt === null ? 'Muddatsiz' : formatDateTime(test.dueAt) }}
              </span>
            </div>
            <div class="inline-flex items-center gap-1.5">
              <AppIcon
                name="users"
                :size="13"
              />
              <span class="tabular-nums">{{ test.attemptCount }} topshirilgan</span>
            </div>
          </dl>

          <!-- E'lon qilishga to'siq bo'lsa sababi shu yerda, tugma yonida. -->
          <p
            v-if="!test.isPublished && publishBlocked !== null"
            class="mt-3 flex items-start gap-2 rounded-lg bg-ink-800 px-3 py-2 text-xs text-slate-300"
          >
            <AppIcon
              name="alert"
              :size="14"
              class="mt-px"
            />
            <span v-text="publishBlocked" />
          </p>

          <p
            v-if="test.isPublished"
            class="mt-3 text-[11px] leading-relaxed text-dim"
          >
            E’lon qilingan test o‘quvchilarga ko‘rinadi: musobaqa — hammaga,
            dars testi — faqat darsi ochilgan o‘quvchiga.
          </p>
        </BaseCard>

        <TestQuestionsEditor
          class="mb-5"
          :test-id="testId"
          :questions="questions"
          :locked="locked"
          @changed="refresh"
        />

        <TestResultsPanel :test-id="testId" />
      </template>
    </DataStatus>

    <TestFormDialog
      :open="editOpen"
      :test="test"
      @close="editOpen = false"
      @saved="refresh"
    />

    <ConfirmDeleteDialog
      :open="deleteOpen"
      title="Testni o‘chirish"
      :message="`“${test === null ? 'Test' : testTitle(test)}” testi savollari bilan o‘chiriladi. O‘quvchi urinishi bo‘lsa server ruxsat bermaydi.`"
      :pending="deleteMutation.isPending.value"
      :error="deleteError"
      @close="deleteOpen = false"
      @confirm="deleteMutation.mutate()"
    />
  </div>
</template>
