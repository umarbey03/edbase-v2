<script setup lang="ts">
import ArabicText from './ArabicText.vue'
import PlayButton from './PlayButton.vue'

/** So'z/ibora kartasi: arabcha, tarjima va tinglash tugmasi. */
defineProps<{ ar: string | null; uz?: string | null; audio?: string | null; extra?: string | null }>()
</script>

<template>
  <div class="flex min-w-0 flex-col gap-1 rounded-xl border border-line bg-ink-900 px-3.5 py-3">
    <div class="flex items-center justify-between gap-2">
      <PlayButton
        :handle="audio"
        :label="uz ?? ar"
        small
      />
      <ArabicText
        :text="ar"
        size="lg"
        class="ms-auto text-right"
      />
    </div>
    <p
      v-if="uz"
      class="text-[13px] text-slate-400"
      v-text="uz"
    />
    <p
      v-if="extra"
      class="text-xs text-amber-400"
      v-text="extra"
    />
  </div>
</template>
