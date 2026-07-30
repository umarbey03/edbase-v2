<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import { isManagerRole } from '@/entities/user'
import { fetchTests, testTitle } from '@/entities/test'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { isApiError, toUserMessage } from '@/shared/api'
import { formatDate } from '@/shared/lib/datetime'
import { BaseBadge, BaseButton, BaseCard, DataStatus, EmptyState } from '@/shared/ui'

/**
 * "Testlar" tabi — eski `#tab-tests` ("Testlar ro'yxati" + guruh natijalari).
 *
 * ★ CHEKLOV, SERVER QOIDASIDAN KELIB CHIQADI (bu yerda takrorlanmaydi,
 * faqat TUSHUNTIRILADI): `TestsController` ning ro'yxat va natijalar
 * amallari `[Authorize(Roles = "Academic,Admin")]` bilan yopiq
 * (`AuthorRoles`). Ya'ni USTOZ bu ma'lumotni umuman ololmaydi — eski
 * ilovada olardi. Tugmani "urinib ko'rsin" deb qoldirish 403 ga olib
 * kelardi, shuning uchun ustozga SABAB ko'rsatiladi.
 *
 * Har o'quvchi bo'yicha test natijasini ustoz baribir ko'ra oladi —
 * "Reyting" tabidagi `Test` ustunida (oylik foiz).
 *
 * ★ Bu tab KURATORGA umuman ko'rsatilmaydi (`visibleGroupTabs`) — eski qoida.
 */
const router = useRouter()
const auth = useAuthStore()

const canReadTests = computed(() => auth.role !== null && isManagerRole(auth.role))

const testsQuery = useQuery({
  queryKey: ['tests', 'list'],
  queryFn: ({ signal }) => fetchTests({ page: 1, pageSize: 50 }, { signal }),
  enabled: canReadTests,
})

const tests = computed(() => testsQuery.data.value?.items ?? [])

const errorMessage = computed(() => {
  const error = testsQuery.error.value
  if (error === null) return null
  if (isApiError(error) && error.isForbidden) return null
  return toUserMessage(error)
})

function openTest(testId: number): void {
  void router.push({ name: 'manage-test', params: { testId: String(testId) } })
}
</script>

<template>
  <BaseCard
    v-if="canReadTests"
    flush
    title="Testlar ro‘yxati"
    subtitle="Onlayn testlar (o‘quv bo‘limi tomonidan yaratilgan)."
  >
    <div class="p-3.5 sm:p-5">
      <DataStatus
        :pending="testsQuery.isPending.value"
        :error="errorMessage"
        :empty="tests.length === 0"
        :retrying="testsQuery.isFetching.value"
        :skeleton-rows="2"
        empty-icon="file-text"
        empty-title="Hozircha testlar yo‘q."
        @retry="testsQuery.refetch()"
      >
        <ul class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          <li
            v-for="test in tests"
            :key="test.id"
            class="flex flex-col justify-between rounded-lg border border-line border-t-[3px] border-t-brand-500 bg-ink-950 p-4"
          >
            <div class="min-w-0">
              <div class="flex flex-wrap items-center gap-2">
                <b
                  class="min-w-0 truncate text-[15px] text-slate-100"
                  v-text="testTitle(test)"
                />
                <BaseBadge :tone="test.isPublished ? 'success' : 'neutral'">
                  {{ test.isPublished ? 'Nashr etilgan' : 'Qoralama' }}
                </BaseBadge>
              </div>
              <p
                class="mt-1 line-clamp-2 text-xs text-slate-400"
                v-text="test.description ?? 'Tavsif yo‘q'"
              />
              <p class="mt-2 text-xs text-slate-400">
                Savollar soni: <b class="tabular-nums text-slate-100">{{ test.questionCount }}</b>
              </p>
              <p
                v-if="test.dueAt !== null"
                class="text-xs text-slate-400"
              >
                Muddati: <b
                  class="tabular-nums text-slate-100"
                >{{ formatDate(test.dueAt) }}</b>
              </p>
            </div>
            <BaseButton
              class="mt-3.5"
              size="sm"
              variant="secondary"
              block
              @click="openTest(test.id)"
            >
              Natijalarni ko‘rish
            </BaseButton>
          </li>
        </ul>
      </DataStatus>
    </div>
  </BaseCard>

  <!--
    Ustoz uchun: sabab OSHKORA. "Bo'sh ro'yxat" ko'rsatish yolg'on bo'lardi —
    testlar bor, lekin ularni bu rol ko'ra olmaydi.
  -->
  <EmptyState
    v-else
    icon="file-text"
    title="Test natijalarini o‘quv bo‘limi ko‘rsatadi"
    text="Serverda testlar ro‘yxati va natijalari faqat o‘quv bo‘limi va administrator uchun ochiq. O‘quvchilaringizning test ko‘rsatkichini “Reyting” tabidagi “Test” ustunida ko‘rishingiz mumkin."
  />
</template>
