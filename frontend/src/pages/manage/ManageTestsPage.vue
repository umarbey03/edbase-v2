<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

import { fetchTests, testKindLabel, testTitle } from '@/entities/test'
import TestFormDialog from '@/features/test-form/ui/TestFormDialog.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateTimeNumeric } from '@/shared/lib/datetime'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import type { TestDto, TestKindName } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  DataStatus,
  PageHeader,
  PaginationBar,
} from '@/shared/ui'

/**
 * Testlar boshqaruvi (Academic/Admin).
 *
 * NEGA FAQAT SHU IKKI ROL: `TestsController` da tuzish amallari
 * `[Authorize(Roles = "Academic,Admin")]` bilan yopiq va `TestService`
 * `LoadAuthorAsync` da qayta tekshiradi — ustoz test tuza olmaydi, chunki
 * test kurs darsiga yoki butun platformaga taalluqli.
 * Marshrutdagi rol ro'yxati shu qoidaning nusxasi, o'rnini bosuvchi emas.
 *
 * NOM BO'YICHA QIDIRUV YO'Q — server `TestListQuery` da faqat `Kind`,
 * `IsPublished` va `ModuleLessonId` ni biladi. Shu sababli bu sahifada
 * qidiruv maydoni ham yo'q (bo'lsa, u yolg'on va'da bo'lardi).
 */
const router = useRouter()
const queryClient = useQueryClient()

/*
  Kartochka ↔ jadval: CSS emas, `v-if` — `hidden lg:block` IKKALA daraxtni
  ham quradi (telefonda ko'rinmas 9 ustunli jadval ham mount bo'lardi).
  ★ Chegara `lg` (1024px), `md` EMAS: yon menyu ham AYNI shu yerda ochiladi,
  ya'ni iPad tik holati (768px) kartochka bo'lib qoladi — `style.css` dagi
  "md va lg haqidagi asosiy qaror" izohiga qarang.
*/
const { isDesktop } = useBreakpoint()

const PAGE_SIZE = 20

const kindFilter = ref<'' | TestKindName>('')
const publishedFilter = ref<'' | 'true' | 'false'>('')
const page = ref(1)

watch([kindFilter, publishedFilter], () => {
  page.value = 1
})

const testsQuery = useQuery({
  queryKey: ['tests', 'manage', kindFilter, publishedFilter, page],
  queryFn: ({ signal }) =>
    fetchTests(
      {
        kind: kindFilter.value === '' ? undefined : kindFilter.value,
        isPublished: publishedFilter.value === '' ? undefined : publishedFilter.value === 'true',
        page: page.value,
        pageSize: PAGE_SIZE,
      },
      { signal },
    ),
})

const tests = computed(() => testsQuery.data.value?.items ?? [])
const total = computed(() => testsQuery.data.value?.total ?? 0)
const totalPages = computed(() => testsQuery.data.value?.totalPages ?? 1)

const errorMessage = computed(() =>
  testsQuery.error.value !== null ? toUserMessage(testsQuery.error.value) : null,
)

const formOpen = ref(false)

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['tests'] })
}

function open(testId: number): void {
  void router.push({ name: 'manage-test', params: { testId: String(testId) } })
}

/**
 * Yangi test yaratilgach DARHOL uning sahifasiga o'tamiz: test savolsiz
 * holda e'lon qilinmaydi, ya'ni keyingi qadam har doim "savol qo'shish".
 */
function handleCreated(test: TestDto): void {
  refresh()
  open(test.id)
}
</script>

<template>
  <div>
    <PageHeader
      title="Testlar"
      :subtitle="`Jami: ${total} ta test`"
    >
      <template #actions>
        <BaseButton @click="formOpen = true">
          <template #icon>
            <AppIcon
              name="plus"
              :size="16"
            />
          </template>
          Yangi
        </BaseButton>
      </template>
    </PageHeader>

    <div class="mb-4 grid gap-2 sm:grid-cols-2 sm:max-w-lg">
      <select
        v-model="kindFilter"
        class="zn-input"
        aria-label="Test turi"
      >
        <option value="">
          Barcha turlar
        </option>
        <option value="Lesson">
          Dars testi
        </option>
        <option value="Competition">
          Musobaqa
        </option>
      </select>
      <select
        v-model="publishedFilter"
        class="zn-input"
        aria-label="E’lon holati"
      >
        <option value="">
          Barcha holatlar
        </option>
        <option value="true">
          E’lon qilingan
        </option>
        <option value="false">
          Qoralama
        </option>
      </select>
    </div>

    <DataStatus
      :pending="testsQuery.isPending.value"
      :error="errorMessage"
      :empty="tests.length === 0"
      :retrying="testsQuery.isFetching.value"
      :skeleton-rows="4"
      empty-icon="award"
      empty-title="Test topilmadi"
      empty-text="Birinchi testni yarating va unga savollar qo‘shing."
      @retry="testsQuery.refetch()"
    >
      <template #empty-action>
        <BaseButton @click="formOpen = true">
          <template #icon>
            <AppIcon
              name="plus"
              :size="16"
            />
          </template>
          Yangi test
        </BaseButton>
      </template>

      <BaseCard flush>
        <!-- Telefon/planshet: kartochka -->
        <ul
          v-if="!isDesktop"
          class="divide-y divide-line"
        >
          <li
            v-for="test in tests"
            :key="test.id"
            class="p-3.5"
          >
            <div class="flex items-start justify-between gap-2">
              <p
                class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
                v-text="testTitle(test)"
              />
              <BaseBadge :tone="test.isPublished ? 'success' : 'neutral'">
                {{ test.isPublished ? 'E’lon qilingan' : 'Qoralama' }}
              </BaseBadge>
            </div>
            <p class="mt-1 text-xs text-slate-400">
              {{ testKindLabel(test.kind) }}
              <span v-if="test.moduleLessonName !== null">· {{ test.moduleLessonName }}</span>
            </p>
            <p class="text-xs tabular-nums text-dim">
              {{ test.questionCount }} savol · {{ test.maxScore }} ball ·
              {{ test.attemptCount }} urinish
            </p>
            <p
              v-if="test.dueAt !== null"
              class="text-xs tabular-nums text-dim"
            >
              Muddat: {{ formatDateTimeNumeric(test.dueAt) }}
            </p>
            <div class="mt-2.5 flex justify-end">
              <BaseButton
                size="sm"
                @click="open(test.id)"
              >
                <template #icon>
                  <AppIcon
                    name="list"
                    :size="13"
                  />
                </template>
                Ochish
              </BaseButton>
            </div>
          </li>
        </ul>

        <!-- Desktop (≥1024px): jadval -->
        <div
          v-else
          class="scroll-x-safe scrollbar-slim"
        >
          <table class="zn-table">
            <thead>
              <tr>
                <th>Sarlavha</th>
                <th>Tur</th>
                <th>Savol</th>
                <th>Ball</th>
                <th>Vaqt</th>
                <th>Muddat</th>
                <th>Urinish</th>
                <th>Holat</th>
                <th />
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="test in tests"
                :key="test.id"
              >
                <td class="font-medium text-slate-100">
                  <p
                    class="max-w-72 truncate"
                    v-text="testTitle(test)"
                  />
                  <p
                    v-if="test.moduleLessonName !== null"
                    class="mt-0.5 max-w-72 truncate text-xs font-normal text-dim"
                    v-text="test.moduleLessonName"
                  />
                </td>
                <td>
                  <BaseBadge :tone="test.kind === 'Lesson' ? 'accent' : 'neutral'">
                    {{ testKindLabel(test.kind) }}
                  </BaseBadge>
                </td>
                <td
                  class="tabular-nums text-slate-400"
                  v-text="test.questionCount"
                />
                <td
                  class="tabular-nums text-slate-400"
                  v-text="test.maxScore"
                />
                <td class="tabular-nums text-slate-400">
                  {{ test.timeLimitMinutes === null ? '—' : `${test.timeLimitMinutes} daq` }}
                </td>
                <td class="tabular-nums text-slate-400">
                  {{ test.dueAt === null ? 'Muddatsiz' : formatDateTimeNumeric(test.dueAt) }}
                </td>
                <td
                  class="tabular-nums text-slate-400"
                  v-text="test.attemptCount"
                />
                <td>
                  <BaseBadge :tone="test.isPublished ? 'success' : 'neutral'">
                    {{ test.isPublished ? 'E’lon qilingan' : 'Qoralama' }}
                  </BaseBadge>
                </td>
                <td class="text-right">
                  <BaseButton
                    size="sm"
                    variant="secondary"
                    @click="open(test.id)"
                  >
                    <template #icon>
                      <AppIcon
                        name="list"
                        :size="13"
                      />
                    </template>
                    Ochish
                  </BaseButton>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <PaginationBar
          :page="page"
          :total-pages="totalPages"
          :total="total"
          @update:page="page = $event"
        />
      </BaseCard>
    </DataStatus>

    <TestFormDialog
      :open="formOpen"
      :test="null"
      @close="formOpen = false"
      @saved="handleCreated"
    />
  </div>
</template>
