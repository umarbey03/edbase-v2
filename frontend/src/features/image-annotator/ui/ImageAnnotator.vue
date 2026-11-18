<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'

import { AppIcon, BaseButton, BaseModal } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  RASM TAHRIRLAGICH (24/7 chat, 2026-11-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasi: yordamchi o'quvchidan rasm qabul qilib, uni TAHRIRLAB
 * qayta yubora olsin (masalan harfdagi xatoni aylana bilan belgilab).
 *
 * ★ VEKTOR AMALLAR RO'YXATI: chizilgan har narsa (`ops`) alohida yoziladi
 *   va kanva har o'zgarishda QAYTA chiziladi. Shu tufayli "orqaga" bitta
 *   `pop()` va sifat yo'qolmaydi (piksel ustidan piksel chizilmaydi).
 * ★ KOORDINATALAR RASM PIKSELIDA: ekrandagi o'lcham boshqacha bo'lsa ham
 *   (telefon), natija asl o'lchamda chiqadi.
 * ★ ASL RASM O'ZGARMAYDI: natija YANGI fayl bo'lib yuboriladi.
 * ★ BRAUZER DIALOGI YO'Q (`prompt` va h.k.): matn uskunasi uchun maydon
 *   uskunalar panelida turadi.
 */
const props = defineProps<{
  /** Tahrirlanadigan rasm; `null` — oyna yopiq. */
  source: Blob | null
  fileName?: string | null
}>()

const emit = defineEmits<{ close: [], done: [file: File] }>()

type Tool = 'pen' | 'arrow' | 'rect' | 'text' | 'crop'
interface Point { x: number, y: number }
type Op =
  | { type: 'pen', color: string, width: number, points: Point[] }
  | { type: 'arrow', color: string, width: number, from: Point, to: Point }
  | { type: 'rect', color: string, width: number, from: Point, to: Point }
  | { type: 'text', color: string, size: number, at: Point, text: string }

/** Chiqish rasmining eng uzun tomoni — chatga yetarli, fayl esa yengil. */
const MAX_SIDE = 2048

const COLORS = ['#ef4444', '#facc15', '#22c55e', '#3b82f6', '#ffffff', '#111827'] as const

const TOOLS: readonly { id: Tool, label: string, icon: 'pen' | 'arrow-up' | 'grid' | 'file-text' | 'maximize' }[] = [
  { id: 'pen', label: 'Qalam', icon: 'pen' },
  { id: 'arrow', label: 'Strelka', icon: 'arrow-up' },
  { id: 'rect', label: 'Ramka', icon: 'grid' },
  { id: 'text', label: 'Matn', icon: 'file-text' },
  { id: 'crop', label: 'Kesish', icon: 'maximize' },
]

const canvas = ref<HTMLCanvasElement | null>(null)
const image = ref<HTMLImageElement | null>(null)
const loadError = ref<string | null>(null)
const busy = ref(false)

const tool = ref<Tool>('pen')
const color = ref<string>(COLORS[0])
const thick = ref(false)
const textValue = ref('')

const ops = ref<Op[]>([])
const draft = ref<Op | null>(null)
const crop = ref<{ from: Point, to: Point } | null>(null)
const cropDraft = ref<{ from: Point, to: Point } | null>(null)

let objectUrl: string | null = null
let scale = 1

const open = computed(() => props.source !== null)

/* ------------------------------------------------------------ yuklash --- */

function reset(): void {
  ops.value = []
  draft.value = null
  crop.value = null
  cropDraft.value = null
  textValue.value = ''
  tool.value = 'pen'
  loadError.value = null
}

function revoke(): void {
  if (objectUrl !== null) URL.revokeObjectURL(objectUrl)
  objectUrl = null
}

watch(() => props.source, async (blob) => {
  revoke()
  image.value = null
  reset()
  if (blob === null) return

  objectUrl = URL.createObjectURL(blob)
  const img = new Image()
  img.decoding = 'async'
  img.onload = async () => {
    image.value = img
    await nextTick()
    setupCanvas()
  }
  img.onerror = () => { loadError.value = 'Rasmni ochib bo‘lmadi — fayl buzilgan yoki rasm emas.' }
  img.src = objectUrl
}, { immediate: true })

onBeforeUnmount(revoke)

function setupCanvas(): void {
  const img = image.value
  const el = canvas.value
  if (img === null || el === null) return
  scale = Math.min(1, MAX_SIDE / Math.max(img.naturalWidth, img.naturalHeight))
  el.width = Math.round(img.naturalWidth * scale)
  el.height = Math.round(img.naturalHeight * scale)
  render()
}

/* ------------------------------------------------------------ chizish --- */

function strokeWidth(): number {
  const el = canvas.value
  const base = el === null ? 4 : Math.max(2, Math.round(Math.max(el.width, el.height) / 300))
  return thick.value ? base * 2 : base
}

function textSize(): number {
  const el = canvas.value
  const base = el === null ? 24 : Math.max(16, Math.round(Math.max(el.width, el.height) / 28))
  return thick.value ? Math.round(base * 1.5) : base
}

function drawOp(ctx: CanvasRenderingContext2D, op: Op): void {
  ctx.strokeStyle = op.color
  ctx.fillStyle = op.color
  ctx.lineCap = 'round'
  ctx.lineJoin = 'round'

  switch (op.type) {
    case 'pen': {
      if (op.points.length === 0) return
      ctx.lineWidth = op.width
      ctx.beginPath()
      ctx.moveTo(op.points[0]!.x, op.points[0]!.y)
      for (const p of op.points.slice(1)) ctx.lineTo(p.x, p.y)
      if (op.points.length === 1) ctx.lineTo(op.points[0]!.x + 0.1, op.points[0]!.y)
      ctx.stroke()
      return
    }
    case 'arrow': {
      const { from, to } = op
      const angle = Math.atan2(to.y - from.y, to.x - from.x)
      const head = op.width * 4
      ctx.lineWidth = op.width
      ctx.beginPath()
      ctx.moveTo(from.x, from.y)
      ctx.lineTo(to.x, to.y)
      ctx.stroke()
      ctx.beginPath()
      ctx.moveTo(to.x, to.y)
      ctx.lineTo(to.x - head * Math.cos(angle - Math.PI / 6), to.y - head * Math.sin(angle - Math.PI / 6))
      ctx.lineTo(to.x - head * Math.cos(angle + Math.PI / 6), to.y - head * Math.sin(angle + Math.PI / 6))
      ctx.closePath()
      ctx.fill()
      return
    }
    case 'rect': {
      ctx.lineWidth = op.width
      ctx.strokeRect(
        Math.min(op.from.x, op.to.x), Math.min(op.from.y, op.to.y),
        Math.abs(op.to.x - op.from.x), Math.abs(op.to.y - op.from.y))
      return
    }
    case 'text': {
      ctx.font = `bold ${op.size}px system-ui, sans-serif`
      ctx.textBaseline = 'top'
      // Har qanday fonda o'qilsin: qora/oq kontur.
      ctx.lineWidth = Math.max(2, op.size / 8)
      ctx.strokeStyle = op.color === '#111827' ? '#ffffff' : '#111827'
      ctx.strokeText(op.text, op.at.x, op.at.y)
      ctx.fillText(op.text, op.at.x, op.at.y)
    }
  }
}

function normRect(r: { from: Point, to: Point }): { x: number, y: number, w: number, h: number } {
  return {
    x: Math.min(r.from.x, r.to.x),
    y: Math.min(r.from.y, r.to.y),
    w: Math.abs(r.to.x - r.from.x),
    h: Math.abs(r.to.y - r.from.y),
  }
}

function render(): void {
  const el = canvas.value
  const img = image.value
  if (el === null || img === null) return
  const ctx = el.getContext('2d')
  if (ctx === null) return

  ctx.clearRect(0, 0, el.width, el.height)
  ctx.drawImage(img, 0, 0, el.width, el.height)
  for (const op of ops.value) drawOp(ctx, op)
  if (draft.value !== null) drawOp(ctx, draft.value)

  // Kesish hududi: tashqarisi xira.
  const area = cropDraft.value ?? crop.value
  if (area !== null) {
    const r = normRect(area)
    ctx.save()
    ctx.fillStyle = 'rgba(0,0,0,0.55)'
    ctx.beginPath()
    ctx.rect(0, 0, el.width, el.height)
    ctx.rect(r.x, r.y, r.w, r.h)
    ctx.fill('evenodd')
    ctx.strokeStyle = '#ffffff'
    ctx.setLineDash([10, 8])
    ctx.lineWidth = 2
    ctx.strokeRect(r.x, r.y, r.w, r.h)
    ctx.restore()
  }
}

watch([ops, draft, crop, cropDraft], render, { deep: true })

/* ------------------------------------------------------------ ko'rsatgich --- */

function toImage(event: PointerEvent): Point {
  const el = canvas.value!
  const rect = el.getBoundingClientRect()
  return {
    x: ((event.clientX - rect.left) / rect.width) * el.width,
    y: ((event.clientY - rect.top) / rect.height) * el.height,
  }
}

let drawing = false

function onDown(event: PointerEvent): void {
  if (canvas.value === null || image.value === null) return
  const p = toImage(event)

  if (tool.value === 'text') {
    const text = textValue.value.trim()
    if (text.length === 0) return
    ops.value = [...ops.value, { type: 'text', color: color.value, size: textSize(), at: p, text }]
    textValue.value = ''
    return
  }

  canvas.value.setPointerCapture(event.pointerId)
  drawing = true

  switch (tool.value) {
    case 'pen':
      draft.value = { type: 'pen', color: color.value, width: strokeWidth(), points: [p] }
      break
    case 'arrow':
    case 'rect':
      draft.value = { type: tool.value, color: color.value, width: strokeWidth(), from: p, to: p }
      break
    case 'crop':
      cropDraft.value = { from: p, to: p }
      break
  }
}

function onMove(event: PointerEvent): void {
  if (!drawing) return
  const p = toImage(event)
  const d = draft.value
  if (d?.type === 'pen') draft.value = { ...d, points: [...d.points, p] }
  else if (d?.type === 'arrow' || d?.type === 'rect') draft.value = { ...d, to: p }
  else if (cropDraft.value !== null) cropDraft.value = { ...cropDraft.value, to: p }
}

function onUp(): void {
  if (!drawing) return
  drawing = false

  if (draft.value !== null) {
    ops.value = [...ops.value, draft.value]
    draft.value = null
  }

  if (cropDraft.value !== null) {
    const r = normRect(cropDraft.value)
    // Tasodifiy bosish — kesish emas.
    crop.value = r.w > 12 && r.h > 12 ? cropDraft.value : crop.value
    cropDraft.value = null
  }
}

/* ------------------------------------------------------------ amallar --- */

function undo(): void {
  if (ops.value.length > 0) ops.value = ops.value.slice(0, -1)
  else crop.value = null
}

const hasChanges = computed(() => ops.value.length > 0 || crop.value !== null)

async function finish(): Promise<void> {
  const el = canvas.value
  if (el === null || image.value === null) return
  busy.value = true
  try {
    // Kesish hududisiz, toza holatda qayta chizamiz (xiralik natijaga tushmasin).
    const area = crop.value
    crop.value = null
    cropDraft.value = null
    await nextTick()
    render()

    let source: HTMLCanvasElement = el
    if (area !== null) {
      const r = normRect(area)
      const out = document.createElement('canvas')
      out.width = Math.max(1, Math.round(r.w))
      out.height = Math.max(1, Math.round(r.h))
      out.getContext('2d')?.drawImage(el, r.x, r.y, r.w, r.h, 0, 0, out.width, out.height)
      source = out
    }

    const blob = await new Promise<Blob | null>((resolve) => source.toBlob(resolve, 'image/jpeg', 0.9))
    if (blob === null) {
      loadError.value = 'Rasmni saqlab bo‘lmadi — qayta urinib ko‘ring.'
      crop.value = area
      return
    }

    const base = (props.fileName ?? 'rasm').replace(/\.[^.]+$/, '')
    emit('done', new File([blob], `${base}-tahrir.jpg`, { type: 'image/jpeg' }))
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <BaseModal
    :open="open"
    title="Rasmni tahrirlash"
    xl
    @close="emit('close')"
  >
    <div class="space-y-3">
      <!-- Uskunalar -->
      <div class="flex flex-wrap items-center gap-2">
        <div
          class="flex overflow-hidden rounded-lg ring-1 ring-inset ring-line"
          role="radiogroup"
          aria-label="Uskuna"
        >
          <button
            v-for="t in TOOLS"
            :key="t.id"
            type="button"
            role="radio"
            :aria-checked="tool === t.id"
            class="flex items-center gap-1.5 px-2.5 py-2 text-xs"
            :class="tool === t.id ? 'bg-brand-500 text-on-brand' : 'bg-ink-800 text-slate-300 hover:bg-ink-700'"
            @click="tool = t.id"
          >
            <AppIcon
              :name="t.icon"
              :size="14"
            />
            <span class="hidden sm:inline">{{ t.label }}</span>
          </button>
        </div>

        <div
          class="flex items-center gap-1.5"
          role="radiogroup"
          aria-label="Rang"
        >
          <button
            v-for="c in COLORS"
            :key="c"
            type="button"
            role="radio"
            :aria-checked="color === c"
            :aria-label="`Rang ${c}`"
            class="size-6 rounded-full ring-2 ring-offset-2 ring-offset-ink-900"
            :class="color === c ? 'ring-brand-500' : 'ring-transparent'"
            :style="{ backgroundColor: c }"
            @click="color = c"
          />
        </div>

        <label class="flex items-center gap-1.5 text-xs text-slate-300">
          <input
            v-model="thick"
            type="checkbox"
          >
          Qalin
        </label>

        <span class="flex-1" />

        <BaseButton
          size="sm"
          variant="ghost"
          :disabled="!hasChanges"
          @click="undo"
        >
          <AppIcon
            name="undo"
            :size="14"
          />
          Orqaga
        </BaseButton>
        <BaseButton
          size="sm"
          variant="ghost"
          :disabled="!hasChanges"
          @click="ops = []; crop = null"
        >
          <AppIcon
            name="trash"
            :size="14"
          />
          Tozalash
        </BaseButton>
      </div>

      <input
        v-if="tool === 'text'"
        v-model="textValue"
        maxlength="80"
        placeholder="Matnni yozing, so‘ng rasmdagi joyni bosing"
        class="w-full rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
      >
      <p
        v-else-if="tool === 'crop'"
        class="text-xs text-slate-400"
      >
        Qoldiriladigan qismni sichqoncha yoki barmoq bilan belgilang.
      </p>

      <p
        v-if="loadError !== null"
        class="rounded-lg bg-rose-500/10 px-3 py-2 text-sm text-rose-200"
        role="alert"
      >
        {{ loadError }}
      </p>

      <div class="flex max-h-[65vh] justify-center overflow-auto rounded-xl bg-black/40 p-2">
        <canvas
          ref="canvas"
          class="max-h-[62vh] max-w-full touch-none select-none"
          :class="tool === 'text' ? 'cursor-text' : 'cursor-crosshair'"
          @pointerdown.prevent="onDown"
          @pointermove="onMove"
          @pointerup="onUp"
          @pointercancel="onUp"
        />
      </div>
    </div>

    <template #footer>
      <div class="flex justify-end gap-2">
        <BaseButton
          variant="ghost"
          @click="emit('close')"
        >
          Bekor
        </BaseButton>
        <BaseButton
          :loading="busy"
          :disabled="image === null"
          @click="finish"
        >
          <AppIcon
            name="send"
            :size="15"
          />
          Tayyor — yuborish
        </BaseButton>
      </div>
    </template>
  </BaseModal>
</template>
