<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, nextTick, ref, watch } from 'vue'

import {
  deliveryLabel,
  deliveryTone,
  fetchAbsenceNotices,
  fetchAbsenceNoticeSummary,
} from '@/entities/absentee'
import { DELIVERY_OPTIONS } from '@/entities/absentee/model/delivery'
import { fetchGroups, groupDisplayName } from '@/entities/group'
import StudentProfileDrawer from '@/features/student-profile/ui/StudentProfileDrawer.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateTimeNumeric } from '@/shared/lib/datetime'
import { useDebounced } from '@/shared/lib/debounce'
import { formatPhone } from '@/shared/lib/phone'
import type { AbsenceDeliveryName, AbsenceNoticeRowDto } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  DataStatus,
  PaginationBar,
} from '@/shared/ui'

import AbsenceNoticeDrawer from './AbsenceNoticeDrawer.vue'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  KELMAGANLARGA YUBORILGAN XABARLAR — JADVAL (2026-08-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * ★ JADVAL, KARTA EMAS (loyiha egasi): qatorlar bir-biri bilan
 * SOLISHTIRILADI ("kimga qo'ng'iroq qilish kerak?"), karta esa har
 * yozuvni alohida o'qishga majbur qilardi. Uzun matnlar — xabar va
 * sabab — jadvalda emas, "Ko'rish" oynasida.
 *
 * ★ ENG MUHIM USTUN — "SABAB": javob yozganlar bilan bog'lanish SHART
 * EMAS, sabab allaqachon ma'lum. Javob bermaganlar esa qo'ng'iroq
 * ro'yxati. Shuning uchun ustun ko'zga tashlanadigan rangda va
 * "Javob bermaganlar" filtri alohida tugma sifatida turadi.
 *
 * ★ PROFIL SHU YERDA OCHILADI, "Foydalanuvchilar" paneliga O'TILMAYDI
 * (loyiha egasi): kurator ro'yxatdagi o'rnini yo'qotmasin.
 *
 * ★ IKKI DRAWER ICHMA-ICH EMAS: "Ko'rish" oynasi ham, profil ham
 * `BaseDrawer` — shuning uchun biri ochilishidan oldin ikkinchisi
 * yopiladi (loyihada ichma-ich drawer TAQIQLANGAN).
 */
const props = withDefaults(
  defineProps<{
    /** Tashqi davr cheklovi (kirmaganlar panelida — o'sha ekrandagi oraliq). */
    from?: string
    to?: string
    titled?: boolean
  }>(),
  { from: undefined, to: undefined, titled: true },
)

const search = ref('')
const debouncedSearch = useDebounced(search)
const delivery = ref<'' | AbsenceDeliveryName>('')
const groupId = ref<number | ''>('')

/** `null` — hammasi; `false` — faqat javob bermaganlar (qo'ng'iroq ro'yxati). */
const replied = ref<boolean | null>(null)

/**
 * Davrni TASHQARIDAN boshqarilishi.
 *
 * ★ NEGA MUHIM: "Darsga kirmaganlar" panelida davr o'sha ekrandagi
 * oraliqdan keladi va uni bu yerda ikkinchi marta so'rash ikkita
 * qarama-qarshi filtr yaratardi. "Xabarlar" panelida esa tashqi oraliq
 * yo'q — u yerda sana maydonlari KERAK.
 */
const externalRange = computed(() => props.from !== undefined || props.to !== undefined)

const ownFrom = ref('')
const ownTo = ref('')

const effectiveFrom = computed(() =>
  externalRange.value ? props.from : (ownFrom.value.length > 0 ? ownFrom.value : undefined),
)
const effectiveTo = computed(() =>
  externalRange.value ? props.to : (ownTo.value.length > 0 ? ownTo.value : undefined),
)

const page = ref(1)
const pageSize = ref(20)
const PAGE_SIZE_OPTIONS = [10, 20, 50, 100] as const

const effectiveSearch = computed(() => {
  const term = debouncedSearch.value.trim()
  return term.length > 0 ? term : undefined
})

const filters = computed(() => ({
  from: effectiveFrom.value,
  to: effectiveTo.value,
  search: effectiveSearch.value,
  delivery: delivery.value === '' ? undefined : delivery.value,
  groupId: groupId.value === '' ? undefined : groupId.value,
  replied: replied.value ?? undefined,
}))

const filtersActive = computed(
  () =>
    effectiveSearch.value !== undefined
    || delivery.value !== ''
    || groupId.value !== ''
    || replied.value !== null
    || ownFrom.value !== ''
    || ownTo.value !== '',
)

function resetFilters(): void {
  search.value = ''
  delivery.value = ''
  groupId.value = ''
  replied.value = null
  ownFrom.value = ''
  ownTo.value = ''
}

/**
 * Guruhlar ro'yxati — filtr uchun.
 *
 * ★ FAQAT XABAR YUBORILGAN guruhlar emas, HAMMASI: ro'yxat qisqarib
 * turishi ("kecha bor edi, bugun yo'q") foydalanuvchini chalg'itardi.
 */
const groupsQuery = useQuery({
  queryKey: ['groups', 'absence-notice-filter'],
  queryFn: ({ signal }) => fetchGroups({ pageSize: 100 }, { signal }),
})

const groups = computed(() => groupsQuery.data.value?.items ?? [])

watch([filters, pageSize], () => {
  page.value = 1
})

const listQuery = useQuery({
  queryKey: ['absence-notices', 'list', filters, page, pageSize],
  queryFn: ({ signal }) =>
    fetchAbsenceNotices(
      { ...filters.value, page: page.value, pageSize: pageSize.value },
      { signal },
    ),
})

/**
 * Kartalar uchun filtr.
 *
 * ★ `delivery` VA `replied` ATAYLAB KIRMAYDI: kartalar AYNAN shu ikki
 * o'lchov bo'yicha bo'linishni ko'rsatadi. Ular filtrga qo'shilsa,
 * "Javob yo'q" tanlanganda karta "javob yo'q: 2, sababini yozgan: 0"
 * deb o'zini takrorlardi va manzarani yo'qotardi.
 */
const summaryFilters = computed(() => ({
  from: effectiveFrom.value,
  to: effectiveTo.value,
  search: effectiveSearch.value,
  groupId: groupId.value === '' ? undefined : groupId.value,
}))

const summaryQuery = useQuery({
  queryKey: ['absence-notices', 'summary', summaryFilters],
  queryFn: ({ signal }) => fetchAbsenceNoticeSummary(summaryFilters.value, { signal }),
})

const rows = computed(() => listQuery.data.value?.items ?? [])
const total = computed(() => listQuery.data.value?.total ?? 0)
const totalPages = computed(() => listQuery.data.value?.totalPages ?? 1)
const effectivePageSize = computed(() => listQuery.data.value?.pageSize ?? pageSize.value)
const summary = computed(() => summaryQuery.data.value ?? null)

const loadError = computed(() =>
  listQuery.error.value !== null ? toUserMessage(listQuery.error.value) : null,
)

/* ------------------------------------------------------------ oynalar */

const detailOpen = ref(false)
const detail = ref<AbsenceNoticeRowDto | null>(null)

const profileOpen = ref(false)
const profileUserId = ref<number | null>(null)
const profileName = ref('')

function openDetail(row: AbsenceNoticeRowDto): void {
  detail.value = row
  detailOpen.value = true
}

/**
 * Profilni ochadi. Tafsilot oynasi ochiq bo'lsa — AVVAL yopiladi:
 * ikki `BaseDrawer` ichma-ich ochilishi taqiqlangan.
 */
async function openProfile(row: AbsenceNoticeRowDto): Promise<void> {
  if (detailOpen.value) {
    detailOpen.value = false
    await nextTick()
  }

  profileUserId.value = row.studentId
  profileName.value = row.studentName
  profileOpen.value = true
}

/** "Bog'langan" ustuni: kim, qaysi kanal orqali. */
function contactedBy(row: AbsenceNoticeRowDto): { label: string; who: string }[] {
  const items: { label: string; who: string }[] = []

  if (row.toTelegram) items.push({ label: 'Telegram', who: row.sentByName })
  if (row.calledByName !== null) items.push({ label: 'Qo‘ng‘iroq', who: row.calledByName })

  return items
}
</script>

<template>
  <div>
    <!-- ═════════════════════ YIG'MA ═════════════════════ -->
    <div
      v-if="summary !== null && summary.total > 0"
      class="mb-4 grid grid-cols-2 gap-2.5 lg:grid-cols-4"
    >
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-xl font-bold tabular-nums text-slate-100"
          v-text="summary.total"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Jami xabar
        </p>
      </div>
      <div class="rounded-xl border border-line border-l-[3px] border-l-emerald-500 bg-ink-900 p-3.5">
        <p
          class="text-xl font-bold tabular-nums text-emerald-400"
          v-text="summary.replied"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Sababini yozgan
        </p>
      </div>
      <!--
        ★ ENG MUHIM RAQAM: kuratorning haqiqiy ish hajmi. "Jami
        yuborildi" emas, aynan javob bermaganlar soni.
      -->
      <div class="rounded-xl border border-line border-l-[3px] border-l-amber-500 bg-ink-900 p-3.5">
        <p
          class="text-xl font-bold tabular-nums text-amber-400"
          v-text="summary.awaitingReply"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Javob yo‘q — qo‘ng‘iroq kerak
        </p>
      </div>
      <div class="rounded-xl border border-line border-l-[3px] border-l-rose-500 bg-ink-900 p-3.5">
        <p
          class="text-xl font-bold tabular-nums text-rose-400"
          v-text="summary.failed"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Yetkazilmadi
        </p>
      </div>
    </div>

    <!-- ═════════════════════ FILTR ═════════════════════ -->
    <div class="mb-4 rounded-2xl border border-line bg-ink-900 p-4">
      <div class="grid gap-2.5 sm:grid-cols-2 lg:grid-cols-4">
        <!--
          Sana maydonlari FAQAT davr tashqaridan boshqarilmaganda:
          "Darsga kirmaganlar" panelida oraliq o'sha ekrandan keladi va
          ikkinchi marta so'ralsa qarama-qarshi filtr yuzaga kelardi.
        -->
        <label
          v-if="!externalRange"
          class="block"
        >
          <span class="mb-1 block text-[11px] font-semibold text-slate-400">Kundan</span>
          <input
            v-model="ownFrom"
            class="zn-input"
            type="date"
          >
        </label>

        <label
          v-if="!externalRange"
          class="block"
        >
          <span class="mb-1 block text-[11px] font-semibold text-slate-400">Kungacha</span>
          <input
            v-model="ownTo"
            class="zn-input"
            type="date"
          >
        </label>

        <label class="block">
          <span class="mb-1 block text-[11px] font-semibold text-slate-400">Qidiruv</span>
          <span class="relative block">
            <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
              <AppIcon
                name="search"
                :size="16"
              />
            </span>
            <input
              v-model="search"
              class="zn-input pl-9"
              placeholder="O‘quvchi, guruh, matn yoki sabab"
            >
          </span>
        </label>

        <label class="block">
          <span class="mb-1 block text-[11px] font-semibold text-slate-400">Guruh</span>
          <select
            v-model="groupId"
            class="zn-input"
          >
            <option value="">
              Barcha guruhlar
            </option>
            <option
              v-for="group in groups"
              :key="group.id"
              :value="group.id"
            >
              {{ groupDisplayName(group) }}
            </option>
          </select>
        </label>

        <label class="block">
          <span class="mb-1 block text-[11px] font-semibold text-slate-400">Yetkazilish</span>
          <select
            v-model="delivery"
            class="zn-input"
          >
            <option value="">
              Barcha holatlar
            </option>
            <option
              v-for="option in DELIVERY_OPTIONS"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </option>
          </select>
        </label>
      </div>

      <div class="mt-3 flex flex-wrap items-center gap-3 border-t border-line pt-3">
        <span class="text-[11px] text-dim">Javob:</span>
        <!--
          ★ ENG KO'P ISHLATILADIGAN FILTR — TUGMA SIFATIDA: kurator har
          kuni "javob bermaganlar" ni ochadi. Uni ochiluvchi ro'yxat
          ichiga yashirish har safar ikki bosishga majbur qilardi.
        -->
        <div
          class="inline-flex gap-1 rounded-xl border border-line bg-ink-800 p-1"
          role="group"
          aria-label="Javob bo‘yicha filtr"
        >
          <button
            v-for="option in [
              { value: null, label: 'Hammasi' },
              { value: false, label: 'Javob yo‘q' },
              { value: true, label: 'Sabab keldi' },
            ]"
            :key="String(option.value)"
            type="button"
            class="rounded-lg px-3 py-1.5 text-xs font-semibold transition-colors"
            :class="
              replied === option.value
                ? 'bg-brand-500 text-on-brand'
                : 'text-slate-400 hover:bg-ink-900 hover:text-slate-100'
            "
            @click="replied = option.value"
          >
            {{ option.label }}
          </button>
        </div>

        <button
          v-if="filtersActive"
          type="button"
          class="tap-target ml-auto flex items-center gap-1 rounded-lg px-2 text-xs font-semibold text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
          @click="resetFilters"
        >
          <AppIcon
            name="close"
            :size="13"
          />
          Tozalash
        </button>
      </div>
    </div>

    <BaseCard
      :title="props.titled ? 'Yuborilgan xabarlar' : undefined"
      flush
    >
      <DataStatus
        :pending="listQuery.isPending.value"
        :error="loadError"
        :empty="rows.length === 0"
        :retrying="listQuery.isFetching.value"
        :skeleton-rows="4"
        empty-icon="send"
        empty-title="Xabar yo‘q"
        empty-text="Bu shartlarga mos yuborilgan xabar topilmadi."
        @retry="listQuery.refetch()"
      >
        <div class="scroll-x-safe scrollbar-slim">
          <table class="zn-table">
            <thead>
              <tr>
                <th class="w-10">
                  #
                </th>
                <th>O‘quvchi</th>
                <th>Guruh · Ustoz</th>
                <th>Aloqa</th>
                <th>Bog‘langan</th>
                <th>Sabab</th>
                <th class="w-24" />
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(row, index) in rows"
                :key="row.id"
              >
                <td
                  class="tabular-nums text-dim"
                  v-text="(page - 1) * effectivePageSize + index + 1"
                />

                <td>
                  <!--
                    ★ ISM — TUGMA: profil SHU YERDA drawer bo'lib
                    ochiladi, boshqa panelga o'tilmaydi.
                  -->
                  <button
                    type="button"
                    class="text-left font-medium text-slate-100 underline-offset-2 hover:text-brand-400 hover:underline"
                    @click="openProfile(row)"
                  >
                    {{ row.studentName }}
                  </button>
                  <span
                    class="mt-0.5 block text-xs tabular-nums text-dim"
                    v-text="formatDateTimeNumeric(row.sessionStart)"
                  />
                </td>

                <td class="max-w-44">
                  <span
                    class="block truncate text-slate-300"
                    v-text="row.groupName"
                  />
                  <span
                    v-if="row.teacherName !== null"
                    class="mt-0.5 block truncate text-xs text-dim"
                    v-text="row.teacherName"
                  />
                </td>

                <td class="whitespace-nowrap">
                  <a
                    v-if="row.studentPhone !== null"
                    :href="`tel:${row.studentPhone.replace(/\s/g, '')}`"
                    class="block text-xs text-slate-300 hover:text-slate-100"
                  >{{ formatPhone(row.studentPhone) }}</a>
                  <a
                    v-if="row.studentTelegram !== null"
                    :href="`https://t.me/${row.studentTelegram}`"
                    target="_blank"
                    rel="noopener"
                    class="mt-0.5 block text-xs text-sky-400 hover:text-sky-300"
                  >@{{ row.studentTelegram }}</a>
                  <span
                    v-if="row.studentPhone === null && row.studentTelegram === null"
                    class="text-xs text-dim"
                  >—</span>
                </td>

                <td class="max-w-40">
                  <span
                    v-for="item in contactedBy(row)"
                    :key="item.label"
                    class="block truncate text-xs"
                  >
                    <span
                      class="text-slate-400"
                      v-text="`${item.label}:`"
                    />
                    <span
                      class="text-slate-300"
                      v-text="item.who"
                    />
                  </span>
                  <BaseBadge
                    v-if="row.deliveryStatus === 'Failed'"
                    tone="danger"
                    class="mt-1"
                  >
                    {{ deliveryLabel(row.deliveryStatus) }}
                  </BaseBadge>
                  <BaseBadge
                    v-else-if="row.deliveryStatus !== 'Sent'"
                    :tone="deliveryTone(row.deliveryStatus)"
                    class="mt-1"
                  >
                    {{ deliveryLabel(row.deliveryStatus) }}
                  </BaseBadge>
                </td>

                <td class="max-w-56">
                  <span
                    v-if="row.replyText !== null"
                    class="block truncate text-xs text-emerald-300"
                    :title="row.replyText"
                    v-text="row.replyText"
                  />
                  <span
                    v-else-if="row.calledAt !== null"
                    class="block truncate text-xs text-slate-400"
                    :title="row.callNote ?? undefined"
                    v-text="row.callNote ?? 'Qo‘ng‘iroq qilingan'"
                  />
                  <BaseBadge
                    v-else
                    tone="warning"
                  >
                    Javob yo‘q
                  </BaseBadge>
                </td>

                <td>
                  <BaseButton
                    size="sm"
                    variant="secondary"
                    @click="openDetail(row)"
                  >
                    Ko‘rish
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
          :page-size-options="PAGE_SIZE_OPTIONS"
          @update:page="page = $event"
          @update:page-size="pageSize = $event"
        />
      </DataStatus>
    </BaseCard>

    <AbsenceNoticeDrawer
      :open="detailOpen"
      :notice="detail"
      @close="detailOpen = false"
    />

    <StudentProfileDrawer
      :open="profileOpen"
      :user-id="profileUserId"
      :fallback-name="profileName"
      @close="profileOpen = false"
    />
  </div>
</template>
