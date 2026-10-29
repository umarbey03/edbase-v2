<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  YOZISH MAYDONI — harf soyasi ustidan barmoq/qalam bilan (2026-10-29)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Birlik rendereridagi `tracePad` ning Vue nusxasi: harf soyasi Amiri
 * shriftida, chiziq — brend rangida. Natija saqlanmaydi va baholanmaydi
 * (mashq maydoni).
 *
 * ★ `touch-action: none` + `setPointerCapture`: telefonda chizish sahifani
 *   aylantirib yubormasin, barmoq maydondan chiqsa ham chiziq uzilmasin.
 * ★ DPR hisobga olinadi — retina ekranda chiziq xira bo'lmasin.
 */
const props = defineProps<{ letter: string | null }>()

const canvas = ref<HTMLCanvasElement | null>(null)
let ctx: CanvasRenderingContext2D | null = null
let drawing = false
let observer: ResizeObserver | null = null

function cssVar(name: string, fallback: string): string {
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim()
  return value.length > 0 ? value : fallback
}

function reset(): void {
  const el = canvas.value
  if (el === null) return

  const rect = el.getBoundingClientRect()
  const dpr = Math.max(1, window.devicePixelRatio || 1)
  el.width = Math.round(rect.width * dpr)
  el.height = Math.round(rect.height * dpr)

  ctx = el.getContext('2d')
  if (ctx === null) return

  ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
  ctx.clearRect(0, 0, rect.width, rect.height)

  // Harf soyasi — kuzatib yozish uchun.
  ctx.font = `${Math.round(rect.height * 0.62)}px ${cssVar('--font-arabic', 'serif')}`
  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'
  ctx.fillStyle = 'rgba(128, 128, 128, 0.16)'
  ctx.fillText(props.letter ?? '', rect.width / 2, rect.height / 2)

  ctx.lineWidth = 6
  ctx.lineCap = 'round'
  ctx.lineJoin = 'round'
  ctx.strokeStyle = cssVar('--color-brand-500', '#00784f')
}

function point(event: PointerEvent): [number, number] {
  const rect = canvas.value!.getBoundingClientRect()
  return [event.clientX - rect.left, event.clientY - rect.top]
}

function down(event: PointerEvent): void {
  if (ctx === null) reset()
  drawing = true
  canvas.value?.setPointerCapture(event.pointerId)
  ctx?.beginPath()
  ctx?.moveTo(...point(event))
}

function move(event: PointerEvent): void {
  if (!drawing || ctx === null) return
  ctx.lineTo(...point(event))
  ctx.stroke()
}

function up(): void {
  drawing = false
}

onMounted(() => {
  reset()
  if (canvas.value !== null && 'ResizeObserver' in window) {
    observer = new ResizeObserver(() => reset())
    observer.observe(canvas.value)
  }
})

onBeforeUnmount(() => observer?.disconnect())
</script>

<template>
  <div class="mt-3 rounded-xl border border-line bg-ink-850 p-2.5">
    <div class="mb-2 flex items-center justify-between gap-2">
      <p class="text-[13px] font-semibold text-slate-200">
        Yozib ko‘ring
      </p>
      <button
        type="button"
        class="min-h-9 rounded-lg border border-line bg-ink-900 px-3 text-[13px] font-semibold text-slate-300 hover:bg-ink-800"
        @click="reset"
      >
        Tozalash
      </button>
    </div>
    <canvas
      ref="canvas"
      class="block h-48 w-full touch-none rounded-lg bg-ink-900"
      :aria-label="`Yozish maydoni: ${letter ?? ''} harfi`"
      role="img"
      @pointerdown="down"
      @pointermove="move"
      @pointerup="up"
      @pointercancel="up"
      @pointerleave="up"
    />
  </div>
</template>
