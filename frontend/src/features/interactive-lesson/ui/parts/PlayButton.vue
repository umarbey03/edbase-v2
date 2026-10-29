<script setup lang="ts">
import { computed } from 'vue'

import { AppIcon } from '@/shared/ui'

import { useLessonContext } from '../../model/context'

/**
 * Tinglash tugmasi (2026-10-29). Belgisi yo'q bo'lsa — chizilmaydi.
 * ★ Kamida 44×44 px teginish maydoni (390 px telefon), `aria-label` bilan.
 * ★ Xato bo'lsa tugmaning o'zi "qayta urinish" ko'rinishiga o'tadi.
 */
const props = withDefaults(defineProps<{ handle: string | null | undefined; label?: string | null; small?: boolean }>(), {
  label: null,
  small: false,
})

const { audio } = useLessonContext()

const playing = computed(() => props.handle != null && audio.current.value === props.handle)
const failed = computed(() => props.handle != null && audio.failed.value === props.handle)

function toggle(): void {
  if (props.handle != null) void audio.play(props.handle)
}
</script>

<template>
  <button
    v-if="handle != null"
    type="button"
    class="inline-flex shrink-0 items-center justify-center rounded-full transition-colors"
    :class="[
      small ? 'size-9' : 'size-11',
      failed
        ? 'bg-rose-500/10 text-rose-400'
        : playing
          ? 'bg-brand-500 text-on-brand'
          : 'bg-brand-500/10 text-brand-500 hover:bg-brand-500 hover:text-on-brand',
    ]"
    :aria-label="failed ? 'Audio yuklanmadi — qayta urinish' : `Tinglash: ${label ?? ''}`"
    :aria-pressed="playing"
    @click.stop="toggle"
  >
    <AppIcon
      :name="failed ? 'refresh' : playing ? 'close' : 'play'"
      :size="small ? 14 : 16"
    />
  </button>
</template>
