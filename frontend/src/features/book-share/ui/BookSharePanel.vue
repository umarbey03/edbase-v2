<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, useId, useTemplateRef, watch } from 'vue'

import { fetchBooks } from '@/entities/book'
import { toUserMessage } from '@/shared/api'
import { formatFileSize } from '@/shared/lib/text'
import { useModalHost } from '@/shared/lib/useModalHost'
import type { BookDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseSpinner, DataStatus } from '@/shared/ui'

import { PEN_COLORS, ZOOM_MAX, ZOOM_MIN } from '../model/useBookBoard'
import type { BoardTool, UseBookBoardResult } from '../model/useBookBoard'

/**
 * KITOB ULASHISH PANELI (2026-09-09) — ustoz uchun to'liq ekran.
 *
 * Ikki holat: kitob tanlanmagan → kutubxona ro'yxati; tanlangan → sahifa
 * taxtasi (preview) + asboblar. Taxtaning o'zi (`canvas`) `useBookBoard`
 * da yashaydi — panel yopilganda ULASHUV DAVOM ETADI, panel faqat
 * boshqaruv oynasi.
 *
 * ★ `BaseSheet` EMAS: u pastdan chiqadigan 55dvh lik varaq; taxtaga esa
 *   butun ekran kerak (telefonda sahifa shundoq ham kichik). Shuning
 *   uchun o'z qatlami, lekin `useModalHost` bilan — ESC, fokus qulfi va
 *   skroll qulfi ilovadagi boshqa oynalar bilan bir xil ishlasin.
 *
 * ★ TEGINISH: `touch-action: none` (canvas klassi) — barmoq harakati
 *   sahifani skroll qilmasin, chizma bo'lsin. Move qurolida bir barmoq
 *   suradi; kattalashtirish tugmalar bilan (pinch keyinroq).
 */
const props = defineProps<{
  open: boolean
  board: UseBookBoardResult
  /** Canvas hozir LiveKit'ga uzatilyaptimi. */
  sharing: boolean
  sharePending: boolean
}>()

const emit = defineEmits<{
  close: []
  'start-share': []
  'stop-share': []
}>()

const panel = ref<HTMLElement | null>(null)
const titleId = useId()

useModalHost({
  open: () => props.open,
  onClose: () => emit('close'),
  panel,
  kind: 'dialog',
})

const board = computed(() => props.board)

/* ------------------------------------------------------------- kitoblar */

const booksQuery = useQuery({
  queryKey: ['books', 'active'],
  queryFn: ({ signal }) => fetchBooks({}, { signal }),
  enabled: computed(() => props.open && board.value.book.value === null),
  staleTime: 60_000,
})

const search = ref('')
const books = computed(() => {
  const list = booksQuery.data.value ?? []
  const q = search.value.trim().toLocaleLowerCase()
  return q.length === 0 ? list : list.filter((b) => b.title.toLocaleLowerCase().includes(q))
})
const booksError = computed(() =>
  booksQuery.error.value !== null ? toUserMessage(booksQuery.error.value) : null,
)

function pickBook(book: BookDto): void {
  void board.value.openBook(book)
}

/* --------------------------------------------------------------- taxta */

const previewHost = useTemplateRef<HTMLDivElement>('previewHost')

watch(
  [() => props.open, () => board.value.book.value, previewHost],
  ([isOpen, book, host]) => {
    if (isOpen && book !== null && host !== null) board.value.mountPreview(host)
    else board.value.unmountPreview()
  },
  { flush: 'post' },
)

const pageInput = ref('')
watch(
  () => board.value.pageNumber.value,
  (page) => {
    pageInput.value = String(page)
  },
  { immediate: true },
)

function commitPageInput(): void {
  const value = Number(pageInput.value)
  if (Number.isInteger(value) && value >= 1) void board.value.goToPage(value)
  else pageInput.value = String(board.value.pageNumber.value)
}

const TOOLS: ReadonlyArray<{ value: BoardTool; label: string; icon: 'hand' | 'pen' | 'highlighter' }> = [
  { value: 'move', label: 'Surish', icon: 'hand' },
  { value: 'pen', label: 'Qalam', icon: 'pen' },
  { value: 'highlighter', label: 'Marker', icon: 'highlighter' },
]

/* ---- pointer: chizma yoki surish ---- */

let activePointer: number | null = null
let lastPoint: { x: number; y: number } | null = null

function onPointerDown(event: PointerEvent): void {
  const host = previewHost.value
  if (host === null || activePointer !== null) return
  activePointer = event.pointerId
  host.setPointerCapture(event.pointerId)
  const point = board.value.toCanvasPoint(event, host)
  lastPoint = point
  if (board.value.tool.value !== 'move') board.value.strokeStart(point)
}

function onPointerMove(event: PointerEvent): void {
  const host = previewHost.value
  if (host === null || event.pointerId !== activePointer) return
  const point = board.value.toCanvasPoint(event, host)
  if (board.value.tool.value === 'move') {
    if (lastPoint !== null) board.value.panBy(point.x - lastPoint.x, point.y - lastPoint.y)
  } else {
    board.value.strokeMove(point)
  }
  lastPoint = point
}

function onPointerUp(event: PointerEvent): void {
  if (event.pointerId !== activePointer) return
  activePointer = null
  lastPoint = null
  if (board.value.tool.value !== 'move') board.value.strokeEnd()
}

const BTN =
  'inline-flex size-10 shrink-0 items-center justify-center rounded-full text-slate-100 transition-colors active:scale-95 disabled:cursor-not-allowed disabled:opacity-40'
const BTN_IDLE = 'bg-ink-750 hover:bg-ink-700'
const BTN_ACTIVE = 'bg-brand-500 text-on-brand'
</script>

<template>
  <Teleport to="body">
    <div
      v-if="props.open"
      class="fixed inset-0 z-50 flex flex-col bg-ink-950 text-slate-100"
      role="presentation"
    >
      <div
        ref="panel"
        class="flex min-h-0 flex-1 flex-col"
        role="dialog"
        aria-modal="true"
        :aria-labelledby="titleId"
        tabindex="-1"
      >
        <!-- ============================================== sarlavha -->
        <header class="flex shrink-0 items-center gap-2 border-b border-line px-3 py-2">
          <AppIcon
            name="book"
            :size="18"
            class="shrink-0 text-brand-400"
          />
          <h2
            :id="titleId"
            class="min-w-0 flex-1 truncate text-sm font-semibold"
            v-text="board.book.value?.title ?? 'Kitobni tanlang'"
          />
          <span
            v-if="props.sharing"
            class="inline-flex items-center gap-1.5 rounded-full bg-rose-500/15 px-2 py-0.5 text-[11px] font-semibold text-rose-300"
          >
            <span class="size-1.5 animate-pulse rounded-full bg-rose-400" />
            Efirda
          </span>
          <button
            v-if="board.book.value !== null"
            type="button"
            class="tap-target rounded-lg px-2 text-xs text-slate-400 hover:text-slate-100"
            title="Boshqa kitob"
            @click="board.closeBook()"
          >
            Boshqa kitob
          </button>
          <button
            type="button"
            class="tap-target flex items-center justify-center rounded-xl text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
            :title="props.sharing ? 'Yig‘ish (ulashuv davom etadi)' : 'Yopish'"
            aria-label="Yopish"
            @click="emit('close')"
          >
            <AppIcon
              name="close"
              :size="18"
            />
          </button>
        </header>

        <!-- ============================================== kutubxona -->
        <div
          v-if="board.book.value === null"
          class="flex min-h-0 flex-1 flex-col gap-3 p-3"
        >
          <input
            v-model="search"
            class="zn-input"
            placeholder="Kitob nomi bo‘yicha qidirish"
          >
          <div class="min-h-0 flex-1 overflow-y-auto scrollbar-slim">
            <DataStatus
              :pending="booksQuery.isPending.value"
              :error="booksError"
              :empty="books.length === 0"
              :retrying="booksQuery.isFetching.value"
              :skeleton-rows="4"
              empty-icon="book"
              empty-title="Kutubxona bo‘sh"
              empty-text="O‘quv bo‘limi «Kutubxona» sahifasidan PDF kitob yuklashi kerak."
              @retry="booksQuery.refetch()"
            >
              <ul class="space-y-2">
                <li
                  v-for="book in books"
                  :key="book.id"
                >
                  <button
                    type="button"
                    class="flex w-full items-center gap-3 rounded-xl border border-line bg-ink-900 p-3 text-left transition-colors hover:border-brand-500/50 hover:bg-ink-800"
                    @click="pickBook(book)"
                  >
                    <span class="flex size-10 shrink-0 items-center justify-center rounded-lg bg-brand-500/15 text-brand-300">
                      <AppIcon
                        name="book"
                        :size="18"
                      />
                    </span>
                    <span class="min-w-0 flex-1">
                      <span
                        class="block truncate text-sm font-medium"
                        v-text="book.title"
                      />
                      <span class="block text-xs text-slate-400">
                        {{ book.pageCount !== null ? `${book.pageCount} sahifa · ` : '' }}{{ formatFileSize(book.sizeBytes) }}
                      </span>
                    </span>
                    <AppIcon
                      name="chevron-right"
                      :size="16"
                      class="shrink-0 text-slate-500"
                    />
                  </button>
                </li>
              </ul>
            </DataStatus>
          </div>
        </div>

        <!-- ============================================== taxta -->
        <template v-else>
          <div class="relative min-h-0 flex-1 bg-ink-900">
            <!--
              Canvas shu konteynerga `mountPreview` bilan ulanadi.
              Pointer hodisalari konteynerda: canvas `object-contain`
              bilan harflashadi va `toCanvasPoint` buni hisobga oladi.
            -->
            <div
              ref="previewHost"
              class="absolute inset-0 touch-none select-none"
              :class="board.tool.value === 'move' ? 'cursor-grab active:cursor-grabbing' : 'cursor-crosshair'"
              @pointerdown.prevent="onPointerDown"
              @pointermove.prevent="onPointerMove"
              @pointerup="onPointerUp"
              @pointercancel="onPointerUp"
            />

            <div
              v-if="board.loading.value"
              class="pointer-events-none absolute inset-0 flex items-center justify-center bg-ink-950/50"
            >
              <BaseSpinner
                size="lg"
                class="text-brand-400"
              />
            </div>

            <p
              v-if="board.error.value !== null"
              class="absolute inset-x-3 top-3 rounded-lg bg-rose-500/15 px-3 py-2 text-xs text-rose-200"
              role="alert"
              v-text="board.error.value"
            />
          </div>

          <!-- ---------------------------------------------- asboblar -->
          <div class="shrink-0 space-y-2 border-t border-line bg-ink-900/95 px-3 py-2 backdrop-blur">
            <!-- 1-qator: sahifa va zoom -->
            <div class="flex items-center justify-between gap-2">
              <div class="flex items-center gap-1.5">
                <button
                  type="button"
                  :class="[BTN, BTN_IDLE]"
                  :disabled="board.pageNumber.value <= 1 || board.loading.value"
                  title="Oldingi sahifa"
                  @click="board.prevPage()"
                >
                  <AppIcon
                    name="chevron-left"
                    :size="18"
                  />
                </button>
                <label class="flex items-center gap-1 text-xs text-slate-300">
                  <input
                    v-model="pageInput"
                    class="zn-input h-9 w-14 px-1 text-center text-sm"
                    inputmode="numeric"
                    @change="commitPageInput"
                    @keydown.enter.prevent="commitPageInput"
                  >
                  <span>/ {{ board.pageCount.value }}</span>
                </label>
                <button
                  type="button"
                  :class="[BTN, BTN_IDLE]"
                  :disabled="board.pageNumber.value >= board.pageCount.value || board.loading.value"
                  title="Keyingi sahifa"
                  @click="board.nextPage()"
                >
                  <AppIcon
                    name="chevron-right"
                    :size="18"
                  />
                </button>
              </div>

              <div class="flex items-center gap-1.5">
                <button
                  type="button"
                  :class="[BTN, BTN_IDLE]"
                  :disabled="board.zoom.value <= ZOOM_MIN"
                  title="Kichiklashtirish"
                  @click="board.zoomOut()"
                >
                  <AppIcon
                    name="zoom-out"
                    :size="18"
                  />
                </button>
                <button
                  type="button"
                  class="min-w-11 rounded-lg px-1 text-xs tabular-nums text-slate-300 hover:bg-ink-800"
                  title="Asl holat"
                  @click="board.resetView()"
                >
                  {{ Math.round(board.zoom.value * 100) }}%
                </button>
                <button
                  type="button"
                  :class="[BTN, BTN_IDLE]"
                  :disabled="board.zoom.value >= ZOOM_MAX"
                  title="Kattalashtirish"
                  @click="board.zoomIn()"
                >
                  <AppIcon
                    name="zoom-in"
                    :size="18"
                  />
                </button>
              </div>
            </div>

            <!-- 2-qator: qurollar, ranglar, orqaga/tozalash, ulashish -->
            <div class="flex flex-wrap items-center gap-1.5">
              <button
                v-for="item in TOOLS"
                :key="item.value"
                type="button"
                :class="[BTN, board.tool.value === item.value ? BTN_ACTIVE : BTN_IDLE]"
                :title="item.label"
                :aria-pressed="board.tool.value === item.value"
                @click="board.tool.value = item.value"
              >
                <AppIcon
                  :name="item.icon"
                  :size="18"
                />
              </button>

              <span
                class="mx-0.5 h-6 w-px bg-line"
                aria-hidden="true"
              />

              <button
                v-for="c in PEN_COLORS"
                :key="c"
                type="button"
                class="size-7 rounded-full ring-2 ring-offset-2 ring-offset-ink-900 transition-transform active:scale-90"
                :class="board.color.value === c && board.tool.value === 'pen' ? 'ring-slate-100' : 'ring-transparent'"
                :style="{ backgroundColor: c }"
                :title="`Rang ${c}`"
                @click="board.color.value = c; board.tool.value = 'pen'"
              />

              <span
                class="mx-0.5 h-6 w-px bg-line"
                aria-hidden="true"
              />

              <button
                type="button"
                :class="[BTN, BTN_IDLE]"
                :disabled="!board.canUndo.value"
                title="Orqaga"
                @click="board.undo()"
              >
                <AppIcon
                  name="undo"
                  :size="18"
                />
              </button>
              <button
                type="button"
                :class="[BTN, BTN_IDLE]"
                :disabled="!board.hasStrokes.value"
                title="Sahifadagi chizmalarni tozalash"
                @click="board.clearPage()"
              >
                <AppIcon
                  name="trash"
                  :size="18"
                />
              </button>

              <div class="ml-auto">
                <BaseButton
                  v-if="!props.sharing"
                  size="sm"
                  :loading="props.sharePending"
                  :disabled="board.loading.value || props.sharePending"
                  @click="emit('start-share')"
                >
                  <template #icon>
                    <AppIcon
                      name="screen-share"
                      :size="15"
                    />
                  </template>
                  Ulashishni boshlash
                </BaseButton>
                <BaseButton
                  v-else
                  size="sm"
                  variant="danger"
                  :loading="props.sharePending"
                  @click="emit('stop-share')"
                >
                  To‘xtatish
                </BaseButton>
              </div>
            </div>
          </div>
        </template>
      </div>
    </div>
  </Teleport>
</template>
