<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, nextTick, ref, useId, watch } from 'vue'
import { useRouter } from 'vue-router'

import { globalSearch, hitIcon, hitRoute } from '@/entities/search'
import { navItemsForRole } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { toUserMessage } from '@/shared/api'
import { useDebounced } from '@/shared/lib/debounce'
import { useModalHost } from '@/shared/lib/useModalHost'
import type { SearchHitDto } from '@/shared/types'
import { AppIcon, BaseSpinner } from '@/shared/ui'
import type { IconName } from '@/shared/ui'

import { highlightParts } from '../model/highlight'
import { clearRecentSearches, recentSearches, rememberSearch } from '../model/recent-searches'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  GLOBAL QIDIRUV OYNASI — "buyruq paneli"
 *  (2026-08-18 · 2026-08-19 da qayta ishlangan)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasi: *"platformani yuqori qismidagi navbarda turishi kerak va
 * bu qismdan platformadagi barcha ma'lumotlarni qidirish imkoni bo'lishi
 * kerak"*, keyin (2026-08-19): *"quyidagi linkdagi search modali kabi
 * bo'lishi kerak"* — `manba-uz` ning ⌘K paneli namuna qilib berildi.
 *
 * Namunadan OLINGAN va NEGA aynan shu olingani:
 *
 * ★ BO'SH OYNA HAM ISH BAJARADI: hech narsa yozilmaganda oxirgi
 *   qidiruvlar va tez o'tish bandlari turadi. Ilgari bu yerda faqat
 *   "nima yozish mumkin" degan izoh bor edi — ya'ni oynani ochishning
 *   o'zi HECH NARSA bermasdi, har safar yozishdan boshlash kerak edi.
 *
 * ★ HAR TUR O'Z SARLAVHASI OSTIDA ("Foydalanuvchilar", "Guruhlar",
 *   "Kurslar"...): ilgari serverdan kelgan barcha natijalar bitta
 *   "Ma'lumotlar" sarlavhasi ostiga tashlanardi va server bergan
 *   `label` umuman ishlatilmasdi. Aralash ro'yxatda o'quvchi bilan
 *   kursni ko'z bilan ajratib bo'lmasdi.
 *
 * ★ MOSLIK BELGILANADI: qatorda AYNAN qaysi qism mos kelgani ko'rinadi
 *   (`highlightParts`). Bir familiyali bir necha o'quvchi chiqqanda
 *   qaysi biri nega chiqqani shusiz noaniq edi.
 *
 * ★ "BARCHA N TA NATIJA": har tur uchun 5 tadan ko'rsatiladi, lekin
 *   server jamini ham beradi (`SearchGroupDto.total`). Ilgari bu son
 *   tashlab yuborilardi — xodim 5 tani ko'rib "boshqa yo'q" deb
 *   xato xulosa qilardi. Bosilganda o'sha turga FILTR qo'yiladi va
 *   20 tagacha ko'rsatiladi.
 *
 * ★ ENG MOS NATIJA ALOHIDA TEPADA: tur bo'yicha guruhlangan ro'yxatda
 *   ismi AYNAN mos kelgan o'quvchi, guruh nomiga qisman mos kelgan
 *   natijadan PASTDA qolishi mumkin edi. Server buni allaqachon
 *   hisoblab beryapti (`topHit`) — ilgari u ham ishlatilmasdi.
 *
 * Namunadan OLINMAGANI: yorug'/qorong'i rejim buyrug'i. Bu ilovada
 * mavzu almashtirgichi yo'q (`style.css` — bitta yorug' tema), ya'ni
 * buyruq hech narsa qilmasdi.
 *
 * Avvalgi qarorlar KUCHIDA qoladi:
 *
 * ★ 250 ms KECHIKISH: har harfda so'rov ketmasin. `queryKey` ichida
 *   AYNAN kechiktirilgan matn turadi — TanStack eski so'rovni bekor
 *   qiladi va javoblar tartibsiz qaytib natijalarni sakratmaydi.
 *
 * ★ SAHIFALAR MIJOZ TOMONIDA: "jarima" deb yozgan xodim ko'pincha
 *   yozuvni emas, PANELNI qidiryapti. Menyu ro'yxati xotirada, serverga
 *   borish keraksiz — shuning uchun sahifa natijalari BIRINCHI harfdayoq
 *   chiqadi, server javobini kutmaydi.
 *
 * ★ ARIA `combobox` + `listbox`: ro'yxat klaviatura bilan boshqariladi,
 *   lekin FOKUS maydonda qoladi — faol qator `aria-activedescendant`
 *   orqali e'lon qilinadi (ekran o'qigichlar uchun yagona to'g'ri naqsh).
 */
const props = defineProps<{ open: boolean }>()

const emit = defineEmits<{ close: [] }>()

const router = useRouter()
const auth = useAuthStore()

/** Serverdagi `GlobalSearchService.MinQueryLength` bilan bir xil. */
const MIN_LENGTH = 2

/** Tur bo'yicha filtr yoqilganda nechta ko'rsatiladi (server chegarasi). */
const FILTERED_LIMIT = 20

const GROUP_RECENT = 'Oxirgi qidiruvlar'
const GROUP_QUICK = 'Tez o‘tish'
const GROUP_PAGES = 'Sahifalar'
const GROUP_TOP = 'Eng mos natija'

const panel = ref<HTMLElement | null>(null)
const input = ref<HTMLInputElement | null>(null)
const listId = useId()

const term = ref('')
const debounced = useDebounced(term, 250)
const activeIndex = ref(0)

/**
 * Tur bo'yicha filtr ("Barcha 34 ta natija" bosilganda).
 *
 * ★ NEGA `label` HAM SAQLANADI: filtr yoqilgan zahoti sarlavhada chip
 * ko'rinishi kerak, lekin server javobi hali kelmagan bo'ladi. Faqat
 * `type` saqlansa, chip bir lahza `users` degan ichki kalitni
 * ko'rsatardi.
 */
const typeFilter = ref<{ type: string; label: string } | null>(null)

useModalHost({
  open: () => props.open,
  onClose: () => emit('close'),
  panel,
  kind: 'dialog',
  closeOnEscape: true,
})

watch(
  () => props.open,
  async (open) => {
    if (!open) return

    term.value = ''
    typeFilter.value = null
    activeIndex.value = 0
    await nextTick()
    input.value?.focus()
  },
)

/* ------------------------------------------------------------ so'rov */

const effectiveTerm = computed(() => {
  const value = debounced.value.trim()
  return value.length >= MIN_LENGTH ? value : ''
})

const searchQuery = useQuery({
  queryKey: ['global-search', effectiveTerm, computed(() => typeFilter.value?.type ?? null)],
  queryFn: ({ signal }) => globalSearch(effectiveTerm.value, {
    signal,
    type: typeFilter.value?.type,
    limit: typeFilter.value !== null ? FILTERED_LIMIT : undefined,
  }),
  enabled: computed(() => props.open && effectiveTerm.value.length > 0),
  // Bir xil matn qayta yozilsa serverga qayta borilmasin.
  staleTime: 30_000,
})

const result = computed(() => searchQuery.data.value ?? null)

const searchError = computed(() =>
  searchQuery.error.value !== null ? toUserMessage(searchQuery.error.value) : null,
)

/** Xato bergan turlar — foydalanuvchi nima ishlamaganini bilsin. */
const failedGroups = computed(() =>
  (result.value?.groups ?? []).filter((group) => group.error !== null),
)

/* ------------------------------------------------------------ sahifalar */

/**
 * Menyu bandlari — MIJOZ TOMONIDA filtrlanadi (serverga borilmaydi).
 * Faqat joriy rolga tegishlilari, ya'ni ro'yxat ruxsatni buzmaydi.
 */
const pageHits = computed(() => {
  const value = term.value.trim().toLocaleLowerCase()
  if (value.length === 0 || typeFilter.value !== null) return []

  return navItemsForRole(auth.role)
    .filter((item) => item.label.toLocaleLowerCase().includes(value))
    .slice(0, 4)
})

/** Bo'sh oynadagi "tez o'tish" ro'yxati — rolning eng boshidagi bandlari. */
const quickPages = computed(() => navItemsForRole(auth.role).slice(0, 6))

/* ------------------------------------------------------------ qatorlar */

interface Row {
  key: string
  /** Klaviatura uchun yassi tartibdagi o'rni. */
  index: number
  label: string
  subtitle: string | null
  meta: string | null
  icon: IconName
  /** Matnda moslik belgilanadimi (tarix/menyu bandida — yo'q). */
  highlight: boolean
  /** Bajarilgach oyna ochiq qoladimi (tarixni tanlash, filtr qo'yish). */
  keepOpen: boolean
  run: () => void
}

interface Section {
  key: string
  label: string
  /** Sarlavha o'ngidagi qo'shimcha — jami natija soni. */
  note: string | null
  rows: Row[]
}

/**
 * ★ NEGA BO'LIMLAR VA YASSI RO'YXAT BIR VAQTDA: chizish uchun bo'limlar
 * kerak (sarlavha bilan), klaviatura uchun esa YASSI tartib — pastga
 * tugmasi sarlavhalarni sakrab o'tishi kerak. `index` bo'limlar
 * yig'ilayotganda AYNI sanoqdan beriladi, shuning uchun ikkalasi hech
 * qachon bir-biridan uzilib qolmaydi.
 */
const sections = computed<Section[]>(() => {
  const list: Section[] = []
  const query = term.value.trim()
  let index = 0

  function section(key: string, label: string, note: string | null): Section {
    const created: Section = { key, label, note, rows: [] }
    list.push(created)
    return created
  }

  function add(
    target: Section,
    row: Omit<Row, 'index' | 'subtitle' | 'meta' | 'highlight' | 'keepOpen'>
      & Partial<Pick<Row, 'subtitle' | 'meta' | 'highlight' | 'keepOpen'>>,
  ): void {
    target.rows.push({
      subtitle: null,
      meta: null,
      highlight: false,
      keepOpen: false,
      ...row,
      index: index++,
    })
  }

  // ═══ 1. Hech narsa yozilmagan: tarix + tez o'tish ═══
  if (query.length === 0 && typeFilter.value === null) {
    if (recentSearches.value.length > 0) {
      const recent = section('recent', GROUP_RECENT, null)

      for (const item of recentSearches.value) {
        add(recent, {
          key: `recent:${item}`,
          label: item,
          icon: 'clock',
          keepOpen: true,
          run: () => useRecent(item),
        })
      }

      add(recent, {
        key: 'recent:clear',
        label: 'Tarixni tozalash',
        icon: 'trash',
        keepOpen: true,
        run: () => clearRecentSearches(),
      })
    }

    if (quickPages.value.length === 0) return list

    const quick = section('quick', GROUP_QUICK, null)

    for (const page of quickPages.value) {
      add(quick, {
        key: `quick:${page.routeName}`,
        label: page.label,
        icon: page.icon,
        run: () => void router.push({ name: page.routeName }),
      })
    }

    return list
  }

  // ═══ 2. Sahifalar — birinchi harfdayoq, serverni kutmasdan ═══
  if (pageHits.value.length > 0) {
    const pages = section('pages', GROUP_PAGES, null)

    for (const page of pageHits.value) {
      add(pages, {
        key: `page:${page.routeName}`,
        label: page.label,
        subtitle: 'Sahifaga o‘tish',
        icon: page.icon,
        highlight: true,
        run: () => openPage(page.routeName),
      })
    }
  }

  const groups = result.value?.groups ?? []
  const hitCount = groups.reduce((sum, group) => sum + group.items.length, 0)
  const topHit = result.value?.topHit ?? null

  // ═══ 3. Eng mos natija ═══
  //
  // Bitta natija bo'lsa ko'rsatilmaydi: u baribir pastdagi bo'limda
  // turadi va ikki marta chizilgani "ikkita topildi" degan taassurot
  // berardi.
  if (typeFilter.value === null && topHit !== null && hitCount > 1) {
    const top = section('top', GROUP_TOP, null)

    add(top, {
      key: `top:${topHit.type}:${topHit.id}`,
      label: topHit.title,
      subtitle: topHit.subtitle,
      meta: topHit.meta,
      icon: hitIcon(topHit.type),
      highlight: true,
      run: () => openHit(topHit),
    })
  }

  // ═══ 4. Tur bo'yicha bo'limlar ═══
  for (const group of groups) {
    if (group.items.length === 0) continue

    const target = section(
      `group:${group.type}`,
      group.label,
      group.total > group.items.length ? String(group.total) : null,
    )

    for (const hit of group.items) {
      add(target, {
        key: `hit:${hit.type}:${hit.id}`,
        label: hit.title,
        subtitle: hit.subtitle,
        meta: hit.meta,
        icon: hitIcon(hit.type),
        highlight: true,
        run: () => openHit(hit),
      })
    }

    // Filtr YOQILGANDA takrorlanmaydi: u yerda allaqachon shu tur
    // ko'rsatilyapti va bosilsa hech narsa o'zgarmasdi.
    if (typeFilter.value === null && group.total > group.items.length) {
      add(target, {
        key: `more:${group.type}`,
        label: `Barcha ${group.total} ta natija`,
        icon: 'arrow-right-left',
        keepOpen: true,
        run: () => applyFilter(group.type, group.label),
      })
    }
  }

  return list
})

const rows = computed<Row[]>(() => sections.value.flatMap((section) => section.rows))

// Natijalar almashsa tanlov birinchi qatorga qaytadi — aks holda
// ko'rsatkich yo'q bo'lib ketgan qatorda "osilib" qolardi.
watch(rows, () => {
  activeIndex.value = 0
})

/* ------------------------------------------------------------ amallar */

function useRecent(value: string): void {
  term.value = value
  input.value?.focus()
}

function applyFilter(type: string, label: string): void {
  typeFilter.value = { type, label }
  activeIndex.value = 0
  input.value?.focus()
}

function clearFilter(): void {
  typeFilter.value = null
  input.value?.focus()
}

function openPage(routeName: string): void {
  rememberSearch(term.value)
  void router.push({ name: routeName })
}

function openHit(hit: SearchHitDto): void {
  const target = hitRoute(hit.type, hit.id)

  rememberSearch(term.value)

  if (target !== null) void router.push(target)
}

/**
 * ★ QATOR O'ZI UZATILADI, `activeIndex` EMAS: sensorli ekranda bosishdan
 * oldin `mousemove` BO'LMAYDI, ya'ni faol qator hamon 0 da turadi.
 * Ilgari bosilgan qator o'rniga ro'yxatning BIRINCHI qatori ochilardi —
 * telefonda qidiruv amalda ishlamasdi.
 */
function runRow(row: Row): void {
  activeIndex.value = row.index

  row.run()

  if (!row.keepOpen) emit('close')
}

function runActive(): void {
  const row = rows.value[activeIndex.value]

  if (row !== undefined) runRow(row)
}

function move(step: number): void {
  const count = rows.value.length
  if (count === 0) return

  // Aylanma harakat: oxirgidan pastga bosilsa birinchisiga qaytadi.
  activeIndex.value = (activeIndex.value + step + count) % count
}

/**
 * Bo'sh maydonda `Backspace` — filtrni olib tashlaydi.
 *
 * ★ NEGA: filtr chipi maydonning ICHIDA turadi va "o'chiriladigan
 * belgi"dek ko'rinadi. Backspace ishlamasa, xodim chipni o'chirmoqchi
 * bo'lib yozgan matnini o'chirib yuborardi.
 */
function onBackspace(event: KeyboardEvent): void {
  if (typeFilter.value === null || term.value.length > 0) return

  event.preventDefault()
  clearFilter()
}

/** Faol qator ko'rinish maydonidan chiqib ketmasin. */
watch(activeIndex, async () => {
  await nextTick()
  document.getElementById(`${listId}-${activeIndex.value}`)?.scrollIntoView({ block: 'nearest' })
})

/* ------------------------------------------------------------ holatlar */

const trimmed = computed(() => term.value.trim())

const showEmpty = computed(() =>
  effectiveTerm.value.length > 0
  && !searchQuery.isFetching.value
  && sections.value.length === 0,
)

/**
 * Bitta harf yozilgan: sahifalar allaqachon chiqadi, lekin server
 * qidiruvi boshlanmaydi. Bu izoh ro'yxat OSTIDA turadi — natijalarni
 * o'chirib qo'ymaydi.
 */
const needsMoreChars = computed(() =>
  trimmed.value.length > 0 && trimmed.value.length < MIN_LENGTH,
)

/** Filtr yoqilgan, lekin qidiriladigan matn yo'q. */
const filterNeedsTerm = computed(() =>
  typeFilter.value !== null && trimmed.value.length === 0,
)

/** macOS'da `⌘`, qolgan joyda `Ctrl` — pastdagi eslatma uchun. */
const cmdKey = computed(() =>
  /mac|iphone|ipad/i.test(navigator.userAgent) ? '⌘' : 'Ctrl',
)
</script>

<template>
  <Teleport to="body">
    <div
      v-if="props.open"
      class="zn-pal-backdrop fixed inset-0 z-50 flex items-start justify-center bg-slate-900/45 px-4 pt-[8vh] backdrop-blur-sm sm:pt-[12vh]"
      @click.self="emit('close')"
    >
      <div
        ref="panel"
        class="zn-pal-panel flex max-h-[80vh] w-full max-w-2xl flex-col overflow-hidden rounded-2xl border border-line bg-ink-900 shadow-2xl"
        role="dialog"
        aria-modal="true"
        aria-label="Global qidiruv"
      >
        <!-- ═══════════════ KIRITISH ═══════════════ -->
        <div class="flex items-center gap-2.5 border-b border-line px-4">
          <BaseSpinner
            v-if="searchQuery.isFetching.value"
            size="sm"
            label="Qidirilmoqda"
            class="shrink-0 text-brand-400"
          />
          <AppIcon
            v-else
            name="search"
            :size="18"
            class="shrink-0 text-slate-500"
          />

          <!--
            FILTR CHIPI: qaysi turda qidirilayotgani doim ko'rinib tursin.
            Chipsiz holatda xodim "nega faqat o'quvchilar chiqyapti?"
            degan savolga javob topa olmasdi.
          -->
          <button
            v-if="typeFilter !== null"
            type="button"
            class="flex shrink-0 items-center gap-1 rounded-md bg-brand-500/14 py-0.5 pl-2 pr-1 text-xs font-semibold text-brand-400 transition-colors hover:bg-brand-500/20"
            :aria-label="`${typeFilter.label} filtrini olib tashlash`"
            @click="clearFilter"
          >
            {{ typeFilter.label }}
            <AppIcon
              name="close"
              :size="12"
            />
          </button>

          <input
            ref="input"
            v-model="term"
            type="text"
            class="h-14 min-w-0 flex-1 bg-transparent text-[15px] text-slate-100 outline-none placeholder:text-slate-500"
            :placeholder="typeFilter !== null
              ? `${typeFilter.label} ichidan qidirish...`
              : 'O‘quvchi, guruh, kurs, test yoki sahifa...'"
            role="combobox"
            aria-expanded="true"
            :aria-controls="listId"
            :aria-activedescendant="rows.length > 0 ? `${listId}-${activeIndex}` : undefined"
            autocomplete="off"
            spellcheck="false"
            @keydown.down.prevent="move(1)"
            @keydown.up.prevent="move(-1)"
            @keydown.home.prevent="activeIndex = 0"
            @keydown.end.prevent="activeIndex = rows.length - 1"
            @keydown.enter.prevent="runActive"
            @keydown.backspace="onBackspace"
          >

          <!--
            "Esc" — HAQIQIY TUGMA, bezak emas: telefonda `Esc` klavishi
            yo'q va oynani yopishning yagona yo'li fon bosish bo'lib
            qolardi (natijalar ro'yxati ekranni to'ldirganda esa fon
            deyarli ko'rinmaydi).
          -->
          <button
            type="button"
            class="zn-kbd shrink-0 px-1.5 py-1.5 transition-colors hover:border-line-strong hover:text-slate-300"
            aria-label="Yopish"
            @click="emit('close')"
          >
            Esc
          </button>
        </div>

        <!-- ═══════════════ NATIJALAR ═══════════════ -->
        <div
          :id="listId"
          class="min-h-0 flex-1 overflow-y-auto scrollbar-slim p-2"
          role="listbox"
          aria-label="Qidiruv natijalari"
        >
          <p
            v-if="searchError !== null"
            class="mx-2 my-3 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-2 text-xs text-rose-200"
            role="alert"
            v-text="searchError"
          />

          <p
            v-else-if="showEmpty"
            class="px-3 py-10 text-center text-sm text-muted"
          >
            <span class="font-semibold text-slate-200">“{{ trimmed }}”</span> bo‘yicha hech narsa topilmadi.
            <span class="mt-1 block text-xs text-dim">
              Ism, telefon raqami, guruh yoki kurs nomini tekshirib ko‘ring.
            </span>
          </p>

          <template v-else>
            <!--
              ★ `role="group"` MAJBURIY: `listbox` ichida `option` lar
              to'g'ridan-to'g'ri yoki `group` ichida turishi kerak.
              Oddiy `<section>` bo'lsa ekran o'qigich uchun ro'yxat
              uzilib qolardi. Sarlavha `aria-hidden` — u guruhning
              `aria-label` i sifatida allaqachon o'qiladi.
            -->
            <section
              v-for="section in sections"
              :key="section.key"
              role="group"
              :aria-label="section.label"
            >
              <p
                class="flex items-center gap-2 px-2.5 pb-1 pt-3 text-[11px] font-bold uppercase tracking-wider text-dim"
                aria-hidden="true"
              >
                {{ section.label }}
                <span
                  v-if="section.note !== null"
                  class="rounded bg-ink-800 px-1.5 py-px text-[10px] font-semibold tabular-nums tracking-normal text-slate-500"
                  v-text="section.note"
                />
              </p>

              <button
                v-for="row in section.rows"
                :id="`${listId}-${row.index}`"
                :key="row.key"
                type="button"
                role="option"
                :aria-selected="activeIndex === row.index"
                class="flex min-h-[42px] w-full items-start gap-3 rounded-lg px-2.5 py-2 text-left transition-colors duration-150"
                :class="activeIndex === row.index ? 'bg-brand-500/14' : 'hover:bg-ink-800'"
                @mousemove="activeIndex = row.index"
                @click="runRow(row)"
              >
                <AppIcon
                  :name="row.icon"
                  :size="16"
                  class="mt-0.5 shrink-0"
                  :class="activeIndex === row.index ? 'text-brand-400' : 'text-slate-500'"
                />
                <span class="min-w-0 flex-1">
                  <span class="block truncate text-[.95rem] text-slate-100">
                    <template v-if="row.highlight">
                      <span
                        v-for="(part, i) in highlightParts(row.label, trimmed)"
                        :key="i"
                        :class="part.hit ? 'zn-pal-mark' : undefined"
                        v-text="part.text"
                      />
                    </template>
                    <template v-else>{{ row.label }}</template>
                  </span>
                  <span
                    v-if="row.subtitle !== null"
                    class="mt-0.5 block truncate text-[13px] text-muted"
                  >
                    <template v-if="row.highlight">
                      <span
                        v-for="(part, i) in highlightParts(row.subtitle, trimmed)"
                        :key="i"
                        :class="part.hit ? 'zn-pal-mark' : undefined"
                        v-text="part.text"
                      />
                    </template>
                    <template v-else>{{ row.subtitle }}</template>
                  </span>
                </span>
                <span
                  v-if="row.meta !== null"
                  class="shrink-0 rounded-md bg-ink-800 px-2 py-0.5 text-[11px] tabular-nums text-slate-400"
                  v-text="row.meta"
                />
              </button>
            </section>

            <!--
              Filtr qo'yilgan, lekin matn o'chirilgan holat. Bu yerda
              ro'yxat BO'SH qoladi (tarix ko'rsatilsa, filtr chipi
              yonida "hamma narsa" ro'yxati turgandek chalg'itardi) —
              shuning uchun keyingi qadam AYTIB qo'yiladi.
            -->
            <p
              v-if="filterNeedsTerm"
              class="px-3 py-10 text-center text-sm text-muted"
            >
              <span class="font-semibold text-slate-200">{{ typeFilter?.label }}</span> ichidan
              qidirish uchun matn kiriting.
              <span class="mt-1 block text-xs text-dim">
                Filtrni olib tashlash uchun <kbd class="zn-kbd">⌫</kbd> bosing.
              </span>
            </p>

            <!--
              Bitta harf yozilgan holat. Sahifalar YUQORIDA allaqachon
              chiqqan bo'lishi mumkin, shuning uchun bu izoh ularni
              almashtirmaydi — ostiga qo'shiladi.
            -->
            <p
              v-else-if="needsMoreChars"
              class="px-3 py-6 text-center text-xs text-dim"
            >
              Ma’lumotlardan qidirish uchun kamida {{ MIN_LENGTH }} ta harf kiriting.
            </p>

            <!--
              ★ YIQILGAN TUR JIMGINA YO'QOLMAYDI: qolgan natijalar
              ko'rsatiladi, lekin nima ishlamagani ham aytiladi — aks
              holda foydalanuvchi "topilmadi" deb noto'g'ri xulosa
              qilardi.
            -->
            <p
              v-for="group in failedGroups"
              :key="group.type"
              class="mx-2 my-2 rounded-lg border border-amber-500/25 bg-amber-500/10 px-3 py-2 text-[11px] text-amber-200"
              v-text="`${group.label} bo‘limini yuklab bo‘lmadi.`"
            />
          </template>
        </div>

        <!-- ═══════════════ KLAVIATURA IZOHI ═══════════════ -->
        <div class="flex flex-wrap items-center gap-x-4 gap-y-1 border-t border-line bg-ink-950 px-4 py-2 text-[11px] text-dim">
          <span><kbd class="zn-kbd">↑</kbd><kbd class="zn-kbd">↓</kbd> tanlash</span>
          <span><kbd class="zn-kbd">↵</kbd> ochish</span>
          <span><kbd class="zn-kbd">Esc</kbd> yopish</span>
          <span class="ml-auto hidden sm:inline">{{ cmdKey }} K</span>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
/*
  Klaviatura tugmasi ko'rinishi — faqat shu oynada ishlatiladi, shuning
  uchun global `style.css` ga chiqarilmadi.
*/
.zn-kbd {
  display: inline-block;
  min-width: 1.25rem;
  padding: 0 0.25rem;
  margin-right: 0.15rem;
  border: 1px solid var(--color-line);
  border-radius: 0.25rem;
  background: var(--color-ink-800);
  font-family: inherit;
  font-size: 10px;
  text-align: center;
  line-height: 1.05rem;
}

/*
  MOSLIK BELGISI.

  ★ `<mark>` EMAS, `<span>` + klass: `<mark>` brauzerning sariq foni
  bilan keladi va uni har temada qaytadan bekor qilish kerak bo'lardi.
  Bu yerda faqat aksent rangi va yupqa fon — qator o'qilishini
  buzmaydigan darajada.
*/
.zn-pal-mark {
  border-radius: 0.15rem;
  background: color-mix(in oklab, var(--color-brand-500) 16%, transparent);
  color: var(--color-brand-400);
  font-weight: 600;
}

/*
  Ochilish animatsiyasi — FAQAT kirishda. Yopilishda kutish kerak
  bo'lsa, oyna "sekin" his qilinardi: xodim Esc bosgach ro'yxat darrov
  ketishi kerak.
*/
.zn-pal-backdrop {
  animation: zn-pal-fade 120ms ease-out;
}

.zn-pal-panel {
  animation: zn-pal-rise 140ms ease-out;
}

@keyframes zn-pal-fade {
  from {
    opacity: 0;
  }
}

@keyframes zn-pal-rise {
  from {
    opacity: 0;
    transform: translateY(-8px) scale(0.98);
  }
}

@media (prefers-reduced-motion: reduce) {
  .zn-pal-backdrop,
  .zn-pal-panel {
    animation: none;
  }
}
</style>
