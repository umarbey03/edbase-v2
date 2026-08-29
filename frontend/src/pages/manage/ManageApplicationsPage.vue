<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  APPLICATION_NOTE_MAX,
  APPLICATION_STATUS_OPTIONS,
  applicationStatusLabel,
  applicationStatusTone,
  fetchEnrollmentApplications,
  updateEnrollmentApplication,
} from '@/entities/enrollment'
import { toUserMessage } from '@/shared/api'
import { formatDateTimeNumeric } from '@/shared/lib/datetime'
import { useDebounced } from '@/shared/lib/debounce'
import type {
  EnrollmentApplicationDto,
  EnrollmentApplicationStatusName,
} from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  BaseDrawer,
  DataStatus,
  PageHeader,
  PaginationBar,
} from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  KURSGA ARIZALAR (2026-08-28)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Landing sahifadagi forma shu yerga tushadi.
 *
 * 🔴 ARIZA — HISOB EMAS. Bu sahifadagi hech qanday amal foydalanuvchi
 *    yaratmaydi: o'quvchini hamon "Foydalanuvchilar" bo'limidan qo'lda
 *    qo'shasiz. Sabab — backenddagi `EnrollmentApplication` izohida
 *    (bot ham aynan shu qoidaga bo'ysunadi: akkaunt yaratmaydi).
 *
 * ★ O'CHIRISH TUGMASI YO'Q VA BO'LMAYDI: "nechta ariza keldi, nechtasi
 *   o'quvchiga aylandi" — markazning asosiy o'lchovi. O'chirilgan qator
 *   uni jimgina buzardi va "bu oy kam ariza keldi" degan noto'g'ri
 *   xulosa berardi. Kerak bo'lmagan ariza «Rad etildi» holatiga o'tadi.
 *
 * ★ STANDART FILTR — «Yangi»: operator bu sahifani AYNAN yangi arizalar
 *   uchun ochadi. Hamma arizani ko'rsatish uni har safar filtrlashga
 *   majbur qilardi.
 * ════════════════════════════════════════════════════════════════════════
 */

const queryClient = useQueryClient()

/* ------------------------------------------------------------ filtrlar */

const status = ref<EnrollmentApplicationStatusName | ''>('New')
const search = ref('')
const page = ref(1)
const pageSize = ref(20)

// Qidiruv har harfda so'rov yubormasin.
const debouncedSearch = useDebounced(search, 350)

// Filtr o'zgarganda birinchi sahifaga qaytamiz — aks holda 5-sahifada
// turgan operator bo'sh ro'yxat ko'rardi.
watch([status, debouncedSearch, pageSize], () => {
  page.value = 1
})

const listQuery = useQuery({
  queryKey: ['enrollment-applications', status, debouncedSearch, page, pageSize],
  queryFn: ({ signal }) =>
    fetchEnrollmentApplications(
      {
        status: status.value === '' ? null : status.value,
        search: debouncedSearch.value.trim().length > 0 ? debouncedSearch.value.trim() : null,
        page: page.value,
        pageSize: pageSize.value,
      },
      { signal },
    ),
})

const rows = computed<EnrollmentApplicationDto[]>(() => listQuery.data.value?.items ?? [])
const total = computed(() => listQuery.data.value?.total ?? 0)
const totalPages = computed(() => listQuery.data.value?.totalPages ?? 0)

/* --------------------------------------------------------- tahrirlash */

/**
 * Ochiq ariza.
 *
 * ★ DRAWER, MODAL EMAS — loyiha kelishuvi: "standart modal" bu o'ngdan
 *   ochiladigan panel (`BaseDrawer`).
 */
const selected = ref<EnrollmentApplicationDto | null>(null)
const draftStatus = ref<EnrollmentApplicationStatusName>('Contacted')
const draftComment = ref('')
const actionError = ref<string | null>(null)

function openRow(row: EnrollmentApplicationDto): void {
  selected.value = row
  actionError.value = null
  draftComment.value = row.comment ?? ''

  /*
    ★ TANLOV OLDINDAN «Bog'lanildi» ga QO'YILADI (agar ariza hali yangi
      bo'lsa): operator bu panelni deyarli har doim qo'ng'iroqdan KEYIN
      ochadi. Holat o'zgarmasa ham qo'lda tanlash mumkin — bu majburlash
      emas, birinchi taxmin.
  */
  draftStatus.value = row.status === 'New' ? 'Contacted' : row.status
}

function closeDrawer(): void {
  selected.value = null
}

const saveMutation = useMutation({
  mutationFn: (input: {
    id: number
    status: EnrollmentApplicationStatusName
    comment: string | null
  }) => updateEnrollmentApplication(input.id, { status: input.status, comment: input.comment }),
  onSuccess: () => {
    void queryClient.invalidateQueries({ queryKey: ['enrollment-applications'] })
    closeDrawer()
  },
  onError: (error: Error) => {
    actionError.value = toUserMessage(error)
  },
})

function save(): void {
  const row = selected.value
  if (row === null) return

  actionError.value = null

  saveMutation.mutate({
    id: row.id,
    status: draftStatus.value,
    comment: draftComment.value.trim().length > 0 ? draftComment.value.trim() : null,
  })
}

const listError = computed(() =>
  listQuery.error.value === null ? null : toUserMessage(listQuery.error.value),
)
</script>

<template>
  <div>
    <PageHeader
      title="Arizalar"
      subtitle="Saytdagi «Kursga yozilish» formasidan kelgan so‘rovlar. Ariza hisob ochmaydi — o‘quvchini «Foydalanuvchilar» bo‘limidan qo‘shasiz."
    />

    <!-- ═════════════════════════════════════════════ FILTRLAR ═══ -->
    <BaseCard class="mb-4">
      <div class="flex flex-wrap items-end gap-3">
        <label class="block min-w-48 flex-1">
          <span class="mb-1.5 block text-xs font-medium text-slate-400">Qidiruv</span>
          <div class="relative">
            <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
              <AppIcon
                name="search"
                :size="16"
              />
            </span>
            <input
              v-model="search"
              type="search"
              placeholder="Ism yoki telefon"
              class="h-10 w-full rounded-lg bg-ink-950 pl-9 pr-3 text-sm text-slate-100 ring-1 ring-inset ring-line-strong transition-colors placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500"
            >
          </div>
        </label>

        <label class="block">
          <span class="mb-1.5 block text-xs font-medium text-slate-400">Holat</span>
          <select
            v-model="status"
            class="h-10 rounded-lg bg-ink-950 px-3 text-sm text-slate-100 ring-1 ring-inset ring-line-strong transition-colors focus:outline-none focus:ring-2 focus:ring-brand-500"
          >
            <option value="">
              Hammasi
            </option>
            <option
              v-for="option in APPLICATION_STATUS_OPTIONS"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </option>
          </select>
        </label>
      </div>
    </BaseCard>

    <!-- ══════════════════════════════════════════════ RO'YXAT ═══ -->
    <DataStatus
      :pending="listQuery.isPending.value"
      :error="listError"
      :empty="rows.length === 0"
      empty-title="Ariza yo‘q"
      empty-text="Tanlangan filtr bo‘yicha ariza topilmadi."
      empty-icon="clipboard"
      :skeleton-rows="6"
    >
      <BaseCard flush>
        <div class="overflow-x-auto">
          <table class="w-full min-w-[46rem] text-sm">
            <thead>
              <tr class="border-b border-line text-left text-xs text-slate-500">
                <th class="px-4 py-3 font-medium">
                  Ism
                </th>
                <th class="px-4 py-3 font-medium">
                  Telefon
                </th>
                <th class="px-4 py-3 font-medium">
                  Yo‘nalish
                </th>
                <th class="px-4 py-3 font-medium">
                  Kelgan vaqti
                </th>
                <th class="px-4 py-3 font-medium">
                  Holat
                </th>
                <th class="px-4 py-3" />
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="row in rows"
                :key="row.id"
                class="border-b border-line last:border-0 transition-colors hover:bg-ink-850"
              >
                <td class="px-4 py-3">
                  <p class="font-medium text-slate-100">
                    {{ row.fullName }}
                  </p>
                  <p
                    v-if="row.note !== null"
                    class="mt-0.5 max-w-xs truncate text-xs text-slate-500"
                    :title="row.note"
                  >
                    {{ row.note }}
                  </p>
                </td>
                <td class="whitespace-nowrap px-4 py-3">
                  <!--
                    `tel:` havolasi — operator qatordan to'g'ridan-to'g'ri
                    qo'ng'iroq qiladi (ish stolida ham, telefonda ham).
                  -->
                  <a
                    class="text-slate-300 transition-colors hover:text-brand-400"
                    :href="`tel:${row.phone}`"
                  >{{ row.phone }}</a>
                </td>
                <td class="px-4 py-3 text-slate-400">
                  {{ row.course ?? '—' }}
                </td>
                <td class="whitespace-nowrap px-4 py-3 text-slate-400">
                  {{ formatDateTimeNumeric(row.createdAt) }}
                </td>
                <td class="px-4 py-3">
                  <BaseBadge :tone="applicationStatusTone(row.status)">
                    {{ applicationStatusLabel(row.status) }}
                  </BaseBadge>
                </td>
                <td class="px-4 py-3 text-right">
                  <BaseButton
                    variant="secondary"
                    size="sm"
                    @click="openRow(row)"
                  >
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
          :page-size="pageSize"
          :page-size-options="[20, 50, 100]"
          @update:page="page = $event"
          @update:page-size="pageSize = $event"
        />
      </BaseCard>
    </DataStatus>

    <!-- ═══════════════════════════════════════════════ PANEL ═══ -->
    <BaseDrawer
      :open="selected !== null"
      title="Ariza"
      @close="closeDrawer"
    >
      <div v-if="selected !== null">
        <dl class="space-y-3">
          <div>
            <dt class="text-xs text-slate-500">
              Ism
            </dt>
            <dd class="text-sm font-medium text-slate-100">
              {{ selected.fullName }}
            </dd>
          </div>
          <div>
            <dt class="text-xs text-slate-500">
              Telefon
            </dt>
            <dd class="text-sm">
              <a
                class="text-brand-400 transition-colors hover:text-brand-300"
                :href="`tel:${selected.phone}`"
              >{{ selected.phone }}</a>
            </dd>
          </div>
          <div>
            <dt class="text-xs text-slate-500">
              Yo‘nalish
            </dt>
            <dd class="text-sm text-slate-300">
              {{ selected.course ?? '—' }}
            </dd>
          </div>
          <div>
            <dt class="text-xs text-slate-500">
              Kelgan vaqti
            </dt>
            <dd class="text-sm text-slate-300">
              {{ formatDateTimeNumeric(selected.createdAt) }}
            </dd>
          </div>
          <div v-if="selected.note !== null">
            <dt class="text-xs text-slate-500">
              Arizachining izohi
            </dt>
            <dd class="whitespace-pre-line text-sm leading-relaxed text-slate-300">
              {{ selected.note }}
            </dd>
          </div>
          <div v-if="selected.handledByName !== null">
            <dt class="text-xs text-slate-500">
              Oxirgi o‘zgarish
            </dt>
            <dd class="text-sm text-slate-300">
              {{ selected.handledByName }}
              <span
                v-if="selected.handledAt !== null"
                class="text-slate-500"
              >· {{ formatDateTimeNumeric(selected.handledAt) }}</span>
            </dd>
          </div>
        </dl>

        <div class="mt-6 border-t border-line pt-5">
          <label class="block">
            <span class="mb-1.5 block text-xs font-medium text-slate-400">Holat</span>
            <select
              v-model="draftStatus"
              class="h-10 w-full rounded-lg bg-ink-950 px-3 text-sm text-slate-100 ring-1 ring-inset ring-line-strong transition-colors focus:outline-none focus:ring-2 focus:ring-brand-500"
            >
              <option
                v-for="option in APPLICATION_STATUS_OPTIONS"
                :key="option.value"
                :value="option.value"
              >
                {{ option.label }}
              </option>
            </select>
          </label>

          <label class="mt-4 block">
            <span class="mb-1.5 block text-xs font-medium text-slate-400">
              Izohingiz <span class="text-slate-600">(qo‘ng‘iroq natijasi)</span>
            </span>
            <textarea
              v-model="draftComment"
              rows="4"
              :maxlength="APPLICATION_NOTE_MAX"
              placeholder="Masalan: kechqurungi guruhga qiziqdi, dushanba kuni keladi"
              class="w-full resize-y rounded-lg bg-ink-950 px-3 py-2.5 text-sm leading-relaxed text-slate-100 ring-1 ring-inset ring-line-strong transition-colors placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500"
            />
          </label>

          <p
            v-if="actionError !== null"
            class="mt-4 rounded-xl bg-rose-500/10 px-3 py-2 text-xs text-rose-200 ring-1 ring-inset ring-rose-500/25"
            role="alert"
            v-text="actionError"
          />

          <div class="mt-5 flex gap-2">
            <BaseButton
              :loading="saveMutation.isPending.value"
              @click="save"
            >
              Saqlash
            </BaseButton>
            <BaseButton
              variant="ghost"
              @click="closeDrawer"
            >
              Bekor qilish
            </BaseButton>
          </div>
        </div>
      </div>
    </BaseDrawer>
  </div>
</template>
