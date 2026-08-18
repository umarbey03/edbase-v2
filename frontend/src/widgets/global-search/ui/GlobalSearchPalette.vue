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
import { AppIcon } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  GLOBAL QIDIRUV OYNASI (2026-08-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasi: *"platformani yuqori qismidagi navbarda turishi kerak va
 * bu qismdan platformadagi barcha ma'lumotlarni qidirish imkoni bo'lishi
 * kerak"*.
 *
 * Tuzilma zamonaviy "command palette" naqshiga tayanadi (Linear, GitHub,
 * Notion) — o'rganilgan xulosalar va NEGA aynan shunday:
 *
 * ★ `Ctrl/⌘+K` — ISHLAB CHIQARISH STANDARTI: xodim boshqa vositalarda
 *   shu kombinatsiyaga o'rgangan. Ilova o'z kombinatsiyasini o'ylab
 *   topsa, u hech qachon barmoq xotirasiga tushmasdi.
 *
 * ★ TUR BO'YICHA GURUHLANADI + "ENG MOS NATIJA" ALOHIDA TEPADA:
 *   faqat guruhlansa, ismi AYNAN mos kelgan o'quvchi guruh nomiga
 *   qisman mos kelgan natijadan pastda qolib ketishi mumkin edi.
 *
 * ★ SAHIFALAR HAM QIDIRILADI (mijoz tomonida): "jarima" deb yozgan
 *   xodim ko'pincha yozuvni emas, PANELNI qidiryapti. Bu uchun serverga
 *   borish keraksiz — menyu ro'yxati allaqachon xotirada.
 *
 * ★ 250 ms KECHIKISH: har bosilgan harfda so'rov yuborilsa, bitta
 *   qidiruvda 10+ so'rov ketardi. `queryKey` ichida AYNAN kechiktirilgan
 *   matn turadi — shu tufayli TanStack eski so'rovni bekor qiladi va
 *   javoblar tartibsiz qaytib natijalarni sakratmaydi.
 *
 * ★ ARIA `combobox` + `listbox`: ro'yxat klaviatura bilan boshqariladi,
 *   lekin FOKUS maydonda qoladi — faol qator `aria-activedescendant`
 *   orqali e'lon qilinadi (ekran o'qigichlar uchun yagona to'g'ri naqsh).
 */
const props = defineProps<{ open: boolean }>()

const emit = defineEmits<{ close: [] }>()

const router = useRouter()
const auth = useAuthStore()

const MIN_LENGTH = 2

const panel = ref<HTMLElement | null>(null)
const input = ref<HTMLInputElement | null>(null)
const listId = useId()

const term = ref('')
const debounced = useDebounced(term, 250)
const activeIndex = ref(0)

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
    activeIndex.value = 0
    await nextTick()
    input.value?.focus()
  },
)

const effectiveTerm = computed(() => {
  const value = debounced.value.trim()
  return value.length >= MIN_LENGTH ? value : ''
})

const searchQuery = useQuery({
  queryKey: ['global-search', effectiveTerm],
  queryFn: ({ signal }) => globalSearch(effectiveTerm.value, { signal }),
  enabled: computed(() => props.open && effectiveTerm.value.length > 0),
  // Bir xil matn qayta yozilsa serverga qayta borilmasin.
  staleTime: 30_000,
})

const result = computed(() => searchQuery.data.value ?? null)

const searchError = computed(() =>
  searchQuery.error.value !== null ? toUserMessage(searchQuery.error.value) : null,
)

/* ------------------------------------------------------------ sahifalar */

/**
 * Menyu bandlari — MIJOZ TOMONIDA filtrlanadi (serverga borilmaydi).
 * Faqat joriy rolga tegishlilari, ya'ni ro'yxat ruxsatni buzmaydi.
 */
const pageHits = computed(() => {
  const value = term.value.trim().toLowerCase()
  if (value.length === 0) return []

  return navItemsForRole(auth.role)
    .filter((item) => item.label.toLowerCase().includes(value))
    .slice(0, 4)
})

/* ------------------------------------------------------------ yassi ro'yxat */

interface FlatRow {
  kind: 'page' | 'hit'
  label: string
  subtitle: string | null
  meta: string | null
  icon: string
  go: () => void
}

/**
 * Klaviatura bilan yurish uchun BARCHA qatorlar bitta yassi ro'yxatda.
 *
 * ★ NEGA YASSI: pastga tugmasi guruh sarlavhalarini "sakrab" o'tishi
 * kerak. Ichma-ich massivda har bosilishda ikki o'lchovli indeks
 * hisoblanardi va chegaralarda xato qilish juda oson bo'lardi.
 */
const rows = computed<FlatRow[]>(() => {
  const list: FlatRow[] = []

  for (const page of pageHits.value) {
    list.push({
      kind: 'page',
      label: page.label,
      subtitle: 'Sahifaga o‘tish',
      meta: null,
      icon: page.icon,
      go: () => void router.push({ name: page.routeName }),
    })
  }

  for (const group of result.value?.groups ?? []) {
    for (const item of group.items) {
      list.push({
        kind: 'hit',
        label: item.title,
        subtitle: item.subtitle,
        meta: item.meta,
        icon: hitIcon(item.type),
        go: () => openHit(item),
      })
    }
  }

  return list
})

// Natijalar almashsa tanlov birinchi qatorga qaytadi — aks holda
// ko'rsatkich yo'q bo'lib ketgan qatorda "osilib" qolardi.
watch(rows, () => {
  activeIndex.value = 0
})

function openHit(hit: SearchHitDto): void {
  const target = hitRoute(hit.type, hit.id)

  if (target !== null) void router.push(target)

  emit('close')
}

function runActive(): void {
  const row = rows.value[activeIndex.value]

  if (row === undefined) return

  row.go()

  if (row.kind === 'page') emit('close')
}

function move(step: number): void {
  const count = rows.value.length
  if (count === 0) return

  // Aylanma harakat: oxirgidan pastga bosilsa birinchisiga qaytadi.
  activeIndex.value = (activeIndex.value + step + count) % count
}

/** Faol qator ko'rinish maydonidan chiqib ketmasin. */
watch(activeIndex, async () => {
  await nextTick()
  document.getElementById(`${listId}-${activeIndex.value}`)?.scrollIntoView({ block: 'nearest' })
})

const showEmpty = computed(() =>
  effectiveTerm.value.length > 0
  && !searchQuery.isFetching.value
  && rows.value.length === 0,
)

/** Xato bergan turlar — foydalanuvchi nima ishlamaganini bilsin. */
const failedGroups = computed(() =>
  (result.value?.groups ?? []).filter((group) => group.error !== null),
)
</script>

<template>
  <Teleport to="body">
    <div
      v-if="props.open"
      class="fixed inset-0 z-50 flex items-start justify-center bg-black/60 px-4 pt-[10vh] backdrop-blur-sm"
      @click.self="emit('close')"
    >
      <div
        ref="panel"
        class="flex max-h-[70vh] w-full max-w-2xl flex-col overflow-hidden rounded-2xl border border-line bg-ink-900 shadow-2xl"
        role="dialog"
        aria-modal="true"
        aria-label="Global qidiruv"
      >
        <!-- ═══════════════ KIRITISH ═══════════════ -->
        <div class="flex items-center gap-2.5 border-b border-line px-4">
          <AppIcon
            name="search"
            :size="18"
            class="shrink-0 text-slate-500"
          />
          <input
            ref="input"
            v-model="term"
            type="text"
            class="min-w-0 flex-1 bg-transparent py-3.5 text-[15px] text-slate-100 outline-none placeholder:text-slate-500"
            placeholder="O‘quvchi, guruh, kurs, test yoki sahifa..."
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
          >
          <span
            v-if="searchQuery.isFetching.value"
            class="shrink-0 text-[11px] text-dim"
          >qidirilmoqda...</span>
          <button
            type="button"
            class="tap-target shrink-0 rounded-lg px-2 text-xs font-semibold text-slate-500 transition-colors hover:text-slate-200"
            @click="emit('close')"
          >
            Esc
          </button>
        </div>

        <!-- ═══════════════ NATIJALAR ═══════════════ -->
        <div
          :id="listId"
          class="min-h-0 flex-1 overflow-y-auto scrollbar-slim py-2"
          role="listbox"
          aria-label="Qidiruv natijalari"
        >
          <!-- Hali yozilmagan: nima qidirsa bo'lishini aytamiz. -->
          <p
            v-if="term.trim().length === 0"
            class="px-4 py-8 text-center text-sm text-dim"
          >
            O‘quvchi ismi, telefon raqami, guruh nomi yoki sahifa nomini yozing.
          </p>

          <p
            v-else-if="term.trim().length < MIN_LENGTH"
            class="px-4 py-8 text-center text-sm text-dim"
          >
            Kamida {{ MIN_LENGTH }} ta harf kiriting.
          </p>

          <p
            v-else-if="searchError !== null"
            class="mx-4 my-3 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-2 text-xs text-rose-200"
            role="alert"
            v-text="searchError"
          />

          <p
            v-else-if="showEmpty"
            class="px-4 py-8 text-center text-sm text-dim"
          >
            <span class="font-semibold text-slate-300">“{{ term.trim() }}”</span> bo‘yicha hech narsa topilmadi.
          </p>

          <template v-else>
            <!--
              ★ ENG MOS NATIJA ALOHIDA TEPADA: guruhlangan ro'yxatda
              aniq mos kelgan yozuv pastda qolib ketishi mumkin edi.
            -->
            <p
              v-if="pageHits.length > 0"
              class="px-4 pb-1 pt-2 text-[11px] font-bold uppercase tracking-wide text-dim"
            >
              Sahifalar
            </p>

            <template
              v-for="(row, index) in rows"
              :key="`${row.kind}-${index}`"
            >
              <!-- Guruh sarlavhasi — birinchi "hit" oldidan bir marta. -->
              <p
                v-if="row.kind === 'hit' && (rows[index - 1]?.kind !== 'hit')"
                class="px-4 pb-1 pt-3 text-[11px] font-bold uppercase tracking-wide text-dim"
              >
                Ma’lumotlar
              </p>

              <button
                :id="`${listId}-${index}`"
                type="button"
                role="option"
                :aria-selected="activeIndex === index"
                class="flex w-full items-center gap-3 px-4 py-2.5 text-left transition-colors"
                :class="activeIndex === index ? 'bg-brand-500/14' : 'hover:bg-ink-800'"
                @mousemove="activeIndex = index"
                @click="runActive"
              >
                <AppIcon
                  :name="(row.icon as never)"
                  :size="16"
                  class="shrink-0"
                  :class="activeIndex === index ? 'text-brand-400' : 'text-slate-500'"
                />
                <span class="min-w-0 flex-1">
                  <span
                    class="block truncate text-sm text-slate-100"
                    v-text="row.label"
                  />
                  <span
                    v-if="row.subtitle !== null"
                    class="block truncate text-xs text-dim"
                    v-text="row.subtitle"
                  />
                </span>
                <span
                  v-if="row.meta !== null"
                  class="shrink-0 rounded-md bg-ink-800 px-2 py-0.5 text-[11px] text-slate-400"
                  v-text="row.meta"
                />
              </button>
            </template>

            <!--
              ★ YIQILGAN TUR JIMGINA YO'QOLMAYDI: qolgan natijalar
              ko'rsatiladi, lekin nima ishlamagani ham aytiladi — aks
              holda foydalanuvchi "topilmadi" deb noto'g'ri xulosa
              qilardi.
            -->
            <p
              v-for="group in failedGroups"
              :key="group.type"
              class="mx-4 my-2 rounded-lg border border-amber-500/25 bg-amber-500/10 px-3 py-2 text-[11px] text-amber-200"
              v-text="`${group.label} bo‘limini yuklab bo‘lmadi.`"
            />
          </template>
        </div>

        <!-- ═══════════════ KLAVIATURA IZOHI ═══════════════ -->
        <div class="flex flex-wrap items-center gap-x-4 gap-y-1 border-t border-line px-4 py-2 text-[11px] text-dim">
          <span><kbd class="zn-kbd">↑</kbd><kbd class="zn-kbd">↓</kbd> tanlash</span>
          <span><kbd class="zn-kbd">Enter</kbd> ochish</span>
          <span><kbd class="zn-kbd">Esc</kbd> yopish</span>
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
</style>
