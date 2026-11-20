<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { CURATOR_WORKSPACE_KEYS, fetchWorkspaceStudents } from '@/entities/curator-workspace'
import { StudentWorkspace, WorkspaceStudentList } from '@/features/curator-workspace'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import { toUserMessage } from '@/shared/api'
import { DataStatus, PageHeader } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  ISH STOLI — kurator (va ustoz) uchun YAGONA oyna (2026-11-20)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasi: *"o'quvchining vazifalari ham, savollari ham, yordamchi
 * qismidan yozganlari ham kuratorga o'quvchi chati orqali kelishi kerak va
 * kurator keyingi darsga dostupni ham shu yer orqali bera olishi kerak"*.
 *
 * Ilgari to'rt sahifa edi: Navbat (faqat raqamlar), Suhbatlar, Tekshirish
 * (vazifa -> o'quvchi), Mustaqil o'quvchilar (darslar xaritasi). Endi:
 * chapda — kim nimani kutyapti; o'ngda — tanlangan o'quvchining hammasi.
 *
 * Tanlangan o'quvchi URL'da (`?student=`): bildirishnoma va havoladan
 * to'g'ridan-to'g'ri kirish, orqaga tugmasi va sahifa yangilash ishlaydi.
 */
const route = useRoute()
const router = useRouter()
const { isDesktop } = useBreakpoint()

const studentsQuery = useQuery({
  queryKey: CURATOR_WORKSPACE_KEYS.students,
  queryFn: ({ signal }) => fetchWorkspaceStudents({ signal }),
  refetchInterval: 30_000,
})

const rows = computed(() => studentsQuery.data.value ?? [])

const selectedId = computed<number | null>(() => {
  const raw = Number(route.query['student'])
  return Number.isInteger(raw) && raw > 0 ? raw : null
})

function select(studentId: number | null): void {
  void router.replace({ query: { ...route.query, student: studentId === null ? undefined : String(studentId) } })
}

// Kompyuterda bo'sh o'ng panel turmasin: eng ko'p ish kutayotgan o'quvchi ochiladi.
watch(
  [rows, isDesktop],
  ([list, desktop]) => {
    if (selectedId.value === null && desktop && list.length > 0) select(list[0]!.studentId)
  },
  { immediate: true },
)

const needsActionCount = computed(() => rows.value.filter((r) => r.needsAction).length)

/**
 * ★ MOBIL (2026-11-20, loyiha egasi: "user ichiga kirganda yozishmalar
 * uchun joy kam qolyapti"): o'quvchi ochilganda sahifa sarlavhasi
 * yashiriladi va panel ekranning qolgan BUTUN balandligini oladi
 * (yuqori panel ~3.75rem + `main` paddingi 2.5rem).
 */
const studentOpenOnMobile = computed(() => !isDesktop.value && selectedId.value !== null)

const error = computed(() => (studentsQuery.error.value ? toUserMessage(studentsQuery.error.value) : null))
</script>

<template>
  <div>
    <PageHeader
      v-if="!studentOpenOnMobile"
      title="Ish stoli"
      :subtitle="needsActionCount > 0
        ? `${needsActionCount} ta o‘quvchi sizni kutmoqda — xabar, vazifa yoki dars qarori.`
        : 'Hammasi bajarilgan. Yangi xabar yoki vazifa kelsa shu yerda chiqadi.'"
    />

    <DataStatus
      :pending="studentsQuery.isPending.value"
      :error="error"
      :empty="false"
      :skeleton-rows="5"
      @retry="studentsQuery.refetch()"
    >
      <div
        class="grid gap-3 lg:h-[calc(100dvh-11rem)] lg:min-h-[520px] lg:grid-cols-[320px_minmax(0,1fr)]"
        :class="studentOpenOnMobile ? 'h-[calc(100dvh-6.5rem)] min-h-[420px]' : 'h-[calc(100dvh-11rem)] min-h-[420px]'"
      >
        <WorkspaceStudentList
          v-show="isDesktop || selectedId === null"
          :rows="rows"
          :selected-id="selectedId"
          @select="select"
        />
        <StudentWorkspace
          v-if="selectedId !== null"
          :key="selectedId"
          :student-id="selectedId"
          @back="select(null)"
        />
        <div
          v-else-if="isDesktop"
          class="flex items-center justify-center rounded-xl border border-dashed border-line text-sm text-slate-500"
        >
          Chapdan o‘quvchini tanlang.
        </div>
      </div>
    </DataStatus>
  </div>
</template>
