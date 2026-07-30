<script setup lang="ts">
import { useToastMessage } from '@/features/student-toast/model/useToast'
import { AppIcon } from '@/shared/ui'

/**
 * Qisqa xabar (eski `.toast`).
 *
 * `role="status"` + `aria-live="polite"`: qulflangan darsni bosgan o'quvchi
 * sababni ko'z bilan ko'radi, ekran o'quvchisi esa eshitadi — aks holda
 * "bosdim, hech nima bo'lmadi" holati yuzaga kelardi.
 */
const message = useToastMessage()
</script>

<template>
  <Teleport to="body">
    <div
      class="pointer-events-none fixed bottom-24 left-1/2 z-[200] -translate-x-1/2 px-4"
      role="status"
      aria-live="polite"
    >
      <p
        v-if="message !== null"
        class="flex max-w-[440px] animate-fade-up items-center gap-2 rounded-3xl border border-line bg-ink-800 px-5 py-3 text-sm text-slate-100 shadow-lg"
      >
        <AppIcon
          name="lock"
          :size="15"
          class="text-brand-500"
        />
        <span v-text="message" />
      </p>
    </div>
  </Teleport>
</template>
