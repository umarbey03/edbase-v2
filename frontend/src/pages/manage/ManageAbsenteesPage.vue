<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { fetchAbsentees, fetchSentNoticeTargets } from '@/entities/absentee'
import { RANGE_PRESETS, daysAgoIso, rangeError, todayIso } from '@/entities/teacher-availability'
import { AbsenceNoticeDialog, AbsenceNoticeHistory } from '@/features/absence-notice'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { toUserMessage } from '@/shared/api'
import { formatDateTimeNumeric, formatTime } from '@/shared/lib/datetime'
import { useDebounced } from '@/shared/lib/debounce'
import { formatPhone } from '@/shared/lib/phone'
import type { AbsenteeStudentDto } from '@/shared/types'
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
 * ════════════════════════════════════════════════════════════════════════
 *  DARSGA KIRMAGANLAR — KUNLIK XARITA (2026-08-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasi: *"bir kun avval darsga kirmagan o'quvchilarni bittada
 * ko'ra olishimiz uchun"*.
 *
 * ★ NEGA MAVJUD DAVOMAT EKRANI YETARLI EMAS: u BITTA DARS kesimida
 * ishlaydi. Kurator esa ertalab "kecha kim kelmadi?" deb so'raydi va
 * buni bilish uchun o'nlab guruhni birma-bir ochishi kerak edi.
 *
 * ★ STANDART SANA — KECHA (bugun emas): bugungi darslarning ko'pi hali
 * o'tmagan bo'ladi va ro'yxat chala chiqardi.
 *
 * ★ KETMA-KET QOLDIRISH — ENG MUHIM USTUN va SARALASH kaliti: bitta
 * qoldirilgan dars odatiy hol, ketma-ket uchtasi esa "bu o'quvchi
 * ketyapti" degan signal. Kurator ro'yxatni yuqoridan pastga qo'ng'iroq
 * qiladi, ya'ni vaqti tugasa qolganlari eng kam xavflilari bo'ladi.
 */

/** Ketma-ket shu sondan ko'p — "xavf" (server bilan AYNI chegara). */
const RISK_STREAK = 3

const auth = useAuthStore()

/**
 * Xabar YUBORISH — o'quv bo'limi va admin (server bilan AYNI qoida).
 * Ustoz/kurator ro'yxatni ko'radi va qo'ng'iroq qiladi, lekin markaz
 * nomidan xabar yuborish qarorini o'quv bo'limi qabul qiladi.
 */
const canSend = computed(() => auth.role === 'Academic' || auth.role === 'Admin')

const TABS = [
  { key: 'list', label: 'Kelmaganlar', icon: 'user-x' },
  { key: 'notices', label: 'Yuborilgan xabarlar', icon: 'send' },
] as const

const activeTab = ref<(typeof TABS)[number]['key']>('list')

/**
 * ★ STANDART — BITTA KUN (kecha): loyiha egasi aynan "bir kun avval"
 * degan savol bilan boshlagan. Oraliq esa qo'shimcha imkoniyat: bir
 * haftalik kesimda "kim tez-tez qoldiryapti" ko'rinadi.
 */
const from = ref(daysAgoIso(1))
const to = ref(daysAgoIso(1))
const search = ref('')
const debouncedSearch = useDebounced(search)
const includePartial = ref(false)
const onlyRisk = ref(false)

const page = ref(1)
const pageSize = ref(20)
const PAGE_SIZE_OPTIONS = [10, 20, 50, 100] as const

const dateError = computed(() => rangeError(from.value, to.value))

const effectiveSearch = computed(() => {
  const term = debouncedSearch.value.trim()
  return term.length > 0 ? term : undefined
})

const filters = computed(() => ({
  from: from.value,
  to: to.value,
  includePartial: includePartial.value,
  minStreak: onlyRisk.value ? RISK_STREAK : 0,
  search: effectiveSearch.value,
}))

// Filtr o'zgarsa birinchi sahifaga qaytiladi — aks holda 3-sahifada
// turgan xodim bo'sh ekran ko'rardi.
watch([filters, pageSize], () => {
  page.value = 1
})

const absenteesQuery = useQuery({
  queryKey: ['absentees', filters, page, pageSize],
  queryFn: ({ signal }) =>
    fetchAbsentees(
      { ...filters.value, page: page.value, pageSize: pageSize.value },
      { signal },
    ),
  enabled: computed(() => dateError.value === null),
})

const report = computed(() => absenteesQuery.data.value ?? null)
const groups = computed(() => report.value?.groups ?? [])

const totalGroups = computed(() => report.value?.totalGroups ?? 0)
const totalPages = computed(() =>
  Math.max(1, Math.ceil(totalGroups.value / (report.value?.pageSize ?? pageSize.value))),
)

/** Guruh ichidagi tartib raqami sahifa bo'ylab UZLUKSIZ davom etadi. */
function groupNumber(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1
}

/** Bir kunlik kesimda sana takrorlanmasin — faqat vaqt ko'rsatiladi. */
const singleDay = computed(() => from.value === to.value)

const loadError = computed(() =>
  absenteesQuery.error.value !== null ? toUserMessage(absenteesQuery.error.value) : null,
)

function applyPreset(preset: { from: () => string; to: () => string }): void {
  from.value = preset.from()
  to.value = preset.to()
}

/* ------------------------------------------------------- xabar yuborish */

/**
 * ★ TANLOV KALITI — `studentId:sessionId`: bitta o'quvchi ikki guruhda
 * dars qoldirgan bo'lishi mumkin va ular ALOHIDA xabarlar (har biri o'z
 * guruhi va vaqti bilan). Faqat `studentId` bo'yicha tanlansa, ikkinchi
 * darsi jimgina tushib qolardi.
 */
const selected = ref(new Set<string>())

function keyOf(studentId: number, sessionId: number): string {
  return `${studentId}:${sessionId}`
}

function toggle(studentId: number, sessionId: number): void {
  const key = keyOf(studentId, sessionId)

  if (selected.value.has(key)) selected.value.delete(key)
  else selected.value.add(key)

  // `Set` mutatsiyasi reaktivlikni ishga tushirmaydi — nusxa yaratiladi.
  selected.value = new Set(selected.value)
}

function isSelected(studentId: number, sessionId: number): boolean {
  return selected.value.has(keyOf(studentId, sessionId))
}

/** Guruhning hammasini tanlash/bekor qilish. */
function toggleGroup(students: { studentId: number; sessionId: number }[]): void {
  const keys = students.map((s) => keyOf(s.studentId, s.sessionId))
  const next = new Set(selected.value)

  // Hammasi tanlangan bo'lsa — bekor qilinadi, aks holda hammasi qo'shiladi.
  if (keys.every((key) => next.has(key))) keys.forEach((key) => next.delete(key))
  else keys.forEach((key) => next.add(key))

  selected.value = next
}

// Filtr o'zgarsa tanlov tozalanadi: ekranda ko'rinmaydigan o'quvchiga
// xabar yuborib qo'yish eng yomon turdagi xato bo'lardi.
watch([filters, page], () => {
  selected.value = new Set()
})

const selectedTargets = computed(() =>
  [...selected.value].map((key) => {
    const [studentId, sessionId] = key.split(':')

    return { studentId: Number(studentId), sessionId: Number(sessionId) }
  }),
)

/** Namuna va ogohlantirish uchun tanlanganlarning to'liq yozuvlari. */
const selectedRows = computed(() =>
  groups.value
    .flatMap((group) => group.students.map((student) => ({ group, student })))
    .filter((row) => isSelected(row.student.studentId, row.student.sessionId)),
)

const selectedWithoutTelegram = computed(
  () => selectedRows.value.filter((row) => !row.student.telegramLinked).length,
)

const noticeOpen = ref(false)

/* --------------------------------------------- "allaqachon yuborilgan" */

const visibleSessionIds = computed(() =>
  [...new Set(groups.value.flatMap((g) => g.students.map((s) => s.sessionId)))],
)

/**
 * Shu ekrandagi darslar bo'yicha allaqachon xabar olganlar.
 *
 * ★ NEGA KERAK: kurator bir odamga ikki marta yozmasin. Belgi
 * bo'lmasa, ro'yxatga qayta kirganda hammasini qaytadan yuborardi.
 */
const sentQuery = useQuery({
  queryKey: ['absence-notices', 'sent', visibleSessionIds],
  queryFn: ({ signal }) => fetchSentNoticeTargets(visibleSessionIds.value, { signal }),
  enabled: computed(() => visibleSessionIds.value.length > 0),
})

/** Kalit → yuborilgan xabarning javob holati. */
const sentMap = computed(
  () => new Map((sentQuery.data.value ?? []).map((t) => [keyOf(t.studentId, t.sessionId), t])),
)

function sentStatus(studentId: number, sessionId: number) {
  return sentMap.value.get(keyOf(studentId, sessionId)) ?? null
}

function onSent(): void {
  selected.value = new Set()
  void sentQuery.refetch()
}

/** Ketma-ket qoldirish nishonining rangi — xavf darajasi bo'yicha. */
function streakTone(count: number): 'neutral' | 'warning' | 'danger' {
  if (count >= RISK_STREAK) return 'danger'
  if (count > 1) return 'warning'

  return 'neutral'
}

function streakLabel(count: number): string {
  return count <= 1 ? '1-marta' : `${count}-marta ketma-ket`
}

/** Telefon raqamdan `tel:` havolasi — bosilganda qo'ng'iroq boshlanadi. */
function telHref(student: AbsenteeStudentDto): string | null {
  return student.phone === null ? null : `tel:${student.phone.replace(/\s/g, '')}`
}
</script>

<template>
  <div>
    <PageHeader
      title="Darsga kirmaganlar"
      subtitle="Bir kunning barcha guruhlari bo‘yicha — qo‘ng‘iroq qilish uchun tayyor ro‘yxat."
    />

    <!--
      ═════════════ BO'LIMLAR ═════════════
      Loyiha egasi: yuborilgan xabarlar AYNAN shu panelda tursin —
      ish o'sha yerda bajariladi (yubordim → ertaga javobini ko'raman).
      AYNI komponent "Xabarlar" panelida ham arxiv sifatida turadi.
    -->
    <div
      class="mb-4 inline-flex gap-1 rounded-2xl border border-line bg-ink-900 p-1"
      role="tablist"
    >
      <button
        v-for="tab in TABS"
        :key="tab.key"
        type="button"
        role="tab"
        :aria-selected="activeTab === tab.key"
        class="flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-semibold transition-colors"
        :class="
          activeTab === tab.key
            ? 'bg-brand-500 text-on-brand'
            : 'text-slate-400 hover:bg-ink-800 hover:text-slate-100'
        "
        @click="activeTab = tab.key"
      >
        <AppIcon
          :name="tab.icon"
          :size="15"
        />
        {{ tab.label }}
      </button>
    </div>

    <AbsenceNoticeHistory
      v-if="activeTab === 'notices'"
      :from="from"
      :to="to"
      :titled="false"
    />

    <template v-else>
      <!-- ═════════════════════ FILTRLAR ═════════════════════ -->
      <div class="mb-4 rounded-2xl border border-line bg-ink-900 p-4">
        <div class="grid gap-2.5 sm:grid-cols-2 lg:grid-cols-4">
          <label class="block">
            <span class="mb-1 block text-[11px] font-semibold text-slate-400">Kundan</span>
            <input
              v-model="from"
              class="zn-input"
              type="date"
              :max="todayIso()"
            >
          </label>

          <label class="block">
            <span class="mb-1 block text-[11px] font-semibold text-slate-400">Kungacha</span>
            <input
              v-model="to"
              class="zn-input"
              type="date"
              :max="todayIso()"
            >
          </label>

          <label class="block sm:col-span-2 lg:col-span-1">
            <span class="mb-1 block text-[11px] font-semibold text-slate-400">O‘quvchi ismi</span>
            <input
              v-model="search"
              class="zn-input"
              placeholder="Qidirish"
            >
          </label>

          <div class="flex flex-wrap items-end gap-4 pb-1">
            <label class="flex cursor-pointer items-center gap-2 text-xs text-slate-300">
              <input
                v-model="onlyRisk"
                type="checkbox"
              >
              Faqat ketma-ket {{ RISK_STREAK }}+ qoldirganlar
            </label>
            <label class="flex cursor-pointer items-center gap-2 text-xs text-slate-300">
              <input
                v-model="includePartial"
                type="checkbox"
              >
              Erta chiqib ketganlar ham
            </label>
          </div>
        </div>

        <div class="mt-3 flex flex-wrap items-center gap-1.5 border-t border-line pt-3">
          <span class="mr-1 text-[11px] text-dim">Tez tanlash:</span>
          <button
            type="button"
            class="rounded-lg border border-line bg-ink-800 px-2.5 py-1 text-xs font-semibold text-slate-400 transition-colors hover:text-slate-100"
            @click="from = daysAgoIso(1); to = daysAgoIso(1)"
          >
            Kecha
          </button>
          <button
            type="button"
            class="rounded-lg border border-line bg-ink-800 px-2.5 py-1 text-xs font-semibold text-slate-400 transition-colors hover:text-slate-100"
            @click="from = todayIso(); to = todayIso()"
          >
            Bugun
          </button>
          <!-- Oraliq shablonlari "Ustozlar holati" paneli bilan AYNI manbadan. -->
          <button
            v-for="preset in RANGE_PRESETS"
            :key="preset.key"
            type="button"
            class="rounded-lg border border-line bg-ink-800 px-2.5 py-1 text-xs font-semibold text-slate-400 transition-colors hover:text-slate-100"
            @click="applyPreset(preset)"
          >
            {{ preset.label }}
          </button>
        </div>

        <p
          v-if="dateError !== null"
          class="mt-2 text-[11px] text-rose-400"
          role="alert"
          v-text="dateError"
        />
      </div>

      <!-- ═════════════════════ YIG'MA ═════════════════════ -->
      <div
        v-if="report !== null"
        class="mb-4 grid grid-cols-2 gap-2.5 lg:grid-cols-4"
      >
        <div class="rounded-xl border border-line bg-ink-900 p-3.5">
          <p
            class="text-2xl font-bold tabular-nums text-slate-100"
            v-text="report.totalAbsent"
          />
          <p class="mt-0.5 text-[11px] text-slate-400">
            Kelmagan o‘quvchi
          </p>
        </div>
        <div class="rounded-xl border border-line border-l-[3px] border-l-rose-500 bg-ink-900 p-3.5">
          <p
            class="text-2xl font-bold tabular-nums text-rose-400"
            v-text="report.riskCount"
          />
          <p class="mt-0.5 text-[11px] text-slate-400">
            Ketma-ket {{ RISK_STREAK }}+ qoldirgan
          </p>
        </div>
        <div class="rounded-xl border border-line bg-ink-900 p-3.5">
          <p
            class="text-2xl font-bold tabular-nums text-slate-100"
            v-text="groups.length"
          />
          <p class="mt-0.5 text-[11px] text-slate-400">
            Guruh
          </p>
        </div>
        <div class="rounded-xl border border-line bg-ink-900 p-3.5">
          <p
            class="text-2xl font-bold tabular-nums text-slate-100"
            v-text="report.sessionCount"
          />
          <p class="mt-0.5 text-[11px] text-slate-400">
            O‘tilgan dars
          </p>
        </div>
      </div>

      <!-- ═════════════════════ GURUHLAR ═════════════════════ -->
      <DataStatus
        :pending="absenteesQuery.isPending.value"
        :error="loadError"
        :empty="groups.length === 0"
        :retrying="absenteesQuery.isFetching.value"
        :skeleton-rows="4"
        empty-icon="check-square"
        empty-title="Hamma kelgan"
        empty-text="Bu kuni darsni qoldirgan o‘quvchi yo‘q (yoki o‘sha kuni dars bo‘lmagan)."
        @retry="absenteesQuery.refetch()"
      >
        <div class="space-y-4">
          <BaseCard
            v-for="(group, groupIndex) in groups"
            :key="group.groupId"
            flush
          >
            <!-- Guruh sarlavhasi — kurator qaysi ro'yxatni oldida turganini bilsin. -->
            <div class="flex flex-wrap items-center gap-x-3 gap-y-1 border-b border-line px-4 py-3">
              <!--
              ★ TARTIB RAQAMI SAHIFA BO'YLAB UZLUKSIZ: 2-sahifada yana
              "1." dan boshlansa, "nechtasini ko'rib chiqdim?" degan
              savolga javob berib bo'lmasdi.
            -->
              <span
                class="w-6 shrink-0 tabular-nums text-sm text-dim"
                v-text="`${groupNumber(groupIndex)}.`"
              />
              <span
                class="font-semibold text-slate-100"
                v-text="group.groupName"
              />
              <span
                v-if="group.teacherName !== null"
                class="text-xs text-dim"
                v-text="`Ustoz: ${group.teacherName}`"
              />
              <span
                v-if="group.assistantName !== null"
                class="text-xs text-dim"
                v-text="`Kurator: ${group.assistantName}`"
              />
              <span class="ml-auto text-sm font-bold tabular-nums text-rose-400">
                {{ group.absentCount }}<span
                  v-if="group.activeMembers > 0"
                  class="text-xs font-normal text-dim"
                >/{{ group.activeMembers }}</span>
              </span>

              <!-- Butun guruhni bir bosishda tanlash — eng ko'p uchraydigan holat. -->
              <button
                v-if="canSend"
                type="button"
                class="rounded-lg border border-line px-2 py-1 text-[11px] font-semibold text-slate-400 transition-colors hover:border-line-strong hover:text-slate-100"
                @click="toggleGroup(group.students)"
              >
                Hammasini tanlash
              </button>
            </div>

            <ul class="divide-y divide-line">
              <li
                v-for="(student, studentIndex) in group.students"
                :key="student.studentId"
                class="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-3"
              >
                <input
                  v-if="canSend"
                  type="checkbox"
                  class="shrink-0"
                  :checked="isSelected(student.studentId, student.sessionId)"
                  :aria-label="`${student.studentName} — xabar yuborish uchun tanlash`"
                  @change="toggle(student.studentId, student.sessionId)"
                >

                <!-- Guruh ichidagi tartib — "5 tadan 3-chisiga qo'ng'iroq qildim". -->
                <span
                  class="w-6 shrink-0 tabular-nums text-xs text-dim"
                  v-text="`${studentIndex + 1}.`"
                />

                <span class="min-w-0 flex-1">
                  <span
                    class="block truncate font-medium text-slate-100"
                    v-text="student.studentName"
                  />
                  <span class="mt-0.5 flex flex-wrap items-center gap-x-2 text-xs text-dim">
                    <!-- Bir kunlik kesimda sana takrorlanmaydi — faqat vaqt. -->
                    <span
                      v-text="singleDay
                        ? formatTime(student.sessionStart)
                        : formatDateTimeNumeric(student.sessionStart)"
                    />
                    <span
                      v-if="student.status === 'Partial'"
                      class="text-amber-400"
                    >erta chiqib ketgan</span>
                    <!--
                    Davr bir kundan uzun bo'lsa — shu davrda nechta dars
                    qoldirgani (bitta qatorda jamlangan).
                  -->
                    <span
                      v-if="!singleDay && student.missedInRange > 1"
                      class="font-semibold text-rose-300"
                      v-text="`bu davrda ${student.missedInRange} ta`"
                    />
                    <span v-text="`30 kunda ${student.missedInLast30Days} ta`" />
                  </span>
                </span>

                <BaseBadge :tone="streakTone(student.consecutiveMisses)">
                  {{ streakLabel(student.consecutiveMisses) }}
                </BaseBadge>

                <!--
                ★ UCH XIL HOLAT, UCH XIL ISH:
                  • sabab keldi   → BOG'LANISH SHART EMAS (yashil);
                  • xabar ketgan, javob yo'q → QO'NG'IROQ QILISH KERAK;
                  • belgisiz      → hali xabar ham yuborilmagan.
                Bu belgi bo'lmasa kurator hammasini birma-bir
                qo'ng'iroq qilardi va vaqtining yarmi bekorga ketardi.
              -->
                <BaseBadge
                  v-if="sentStatus(student.studentId, student.sessionId)?.replied === true"
                  tone="success"
                >
                  Sabab keldi
                </BaseBadge>
                <BaseBadge
                  v-else-if="sentStatus(student.studentId, student.sessionId) !== null"
                  tone="warning"
                >
                  Xabar ketdi · javob yo‘q
                </BaseBadge>

                <!--
                ★ QO'NG'IROQ RO'YXATDAN CHIQMASDAN: telefonda `tel:`
                havolasi to'g'ridan-to'g'ri terishni ochadi. Kurator
                raqamni ko'chirib olish uchun profilga o'tmasin.
              -->
                <a
                  v-if="telHref(student) !== null"
                  :href="telHref(student)!"
                  class="tap-target flex items-center gap-1.5 rounded-lg border border-line bg-ink-800 px-2.5 text-xs font-semibold text-slate-300 transition-colors hover:border-line-strong hover:text-slate-100"
                >
                  <AppIcon
                    name="phone"
                    :size="13"
                  />
                  {{ formatPhone(student.phone!) }}
                </a>
                <span
                  v-else
                  class="text-xs text-dim"
                >Telefon yo‘q</span>

                <!-- `AppIcon` `aria-label` propini olmaydi — sarlavha o'ram ustida. -->
                <span
                  v-if="student.telegramLinked"
                  class="flex items-center text-sky-400"
                  title="Telegram ulangan — xabar yuborish mumkin"
                >
                  <AppIcon
                    name="send"
                    :size="14"
                  />
                </span>

                <!--
                  ★ SABAB MATNI TO'LIQ QATORDA: bu — kuratorning
                  qo'ng'iroq qilish yoki qilmaslik qarori. Faqat
                  nishonda (tooltipda) qolsa, ro'yxatni ko'zdan
                  kechirayotgan xodim uni umuman ko'rmasdi.
                -->
                <p
                  v-if="sentStatus(student.studentId, student.sessionId)?.replyText"
                  class="basis-full rounded-lg border border-emerald-500/25 bg-emerald-500/10 px-2.5 py-1.5 text-xs text-emerald-200"
                  v-text="`Sababi: ${sentStatus(student.studentId, student.sessionId)?.replyText}`"
                />
              </li>
            </ul>
          </BaseCard>

          <!--
          ★ SAHIFALASH GURUH BO'YICHA (o'quvchi emas): ro'yxat guruhlarga
          bo'lingan va qo'ng'iroqlar ham guruh bo'yicha taqsimlanadi.
          O'quvchi bo'yicha sahifalansa, bitta guruh ikki sahifaga
          bo'linib, kurator uni ikki marta ochishi kerak bo'lardi.
        -->
          <PaginationBar
            :page="page"
            :total-pages="totalPages"
            :total="totalGroups"
            :page-size="pageSize"
            :page-size-options="PAGE_SIZE_OPTIONS"
            @update:page="page = $event"
            @update:page-size="pageSize = $event"
          />
        </div>
      </DataStatus>
    </template>

    <!--
      ═════════════ TANLANGANLAR PANELI ═════════════
      ★ EKRAN OSTIDA YOPISHIB TURADI: uzun ro'yxatda tanlab pastga
      tushgan kurator tugmani qidirib yuqoriga qaytmasin.
    -->
    <div
      v-if="activeTab === 'list' && selected.size > 0 && canSend"
      class="sticky bottom-4 z-20 mt-4 flex flex-wrap items-center gap-3 rounded-2xl border border-brand-500/40 bg-ink-900 px-4 py-3 shadow-lg"
    >
      <span class="text-sm text-slate-200">
        <span
          class="font-bold text-brand-400"
          v-text="selected.size"
        /> ta o‘quvchi tanlandi
      </span>
      <span
        v-if="selectedWithoutTelegram > 0"
        class="text-xs text-amber-400"
        v-text="`${selectedWithoutTelegram} tasida Telegram yo‘q`"
      />
      <button
        type="button"
        class="text-xs font-semibold text-slate-400 transition-colors hover:text-slate-100"
        @click="selected = new Set()"
      >
        Bekor qilish
      </button>
      <BaseButton
        class="ml-auto"
        @click="noticeOpen = true"
      >
        <template #icon>
          <AppIcon
            name="send"
            :size="15"
          />
        </template>
        Xabar yuborish
      </BaseButton>
    </div>

    <AbsenceNoticeDialog
      :open="noticeOpen"
      :targets="selectedTargets"
      :sample-name="selectedRows[0]?.student.studentName"
      :sample-group="selectedRows[0]?.group.groupName"
      :without-telegram="selectedWithoutTelegram"
      @close="noticeOpen = false"
      @sent="onSent"
    />
  </div>
</template>
