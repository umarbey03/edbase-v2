<script setup lang="ts">
import type { DataTableDto } from '@/entities/interactive-unit'

import { hasArabic } from '../../model/context'
import ArabicText from './ArabicText.vue'
import PlayButton from './PlayButton.vue'

/**
 * Kitob jadvali (masalan, alifbo). Har qatorga tinglash tugmasi (bo'lsa).
 * ★ Jadval o'z ichida gorizontal aylanadi — 390 px da sahifa kengaymaydi.
 */
defineProps<{ table: DataTableDto }>()
</script>

<template>
  <figure class="mt-4">
    <figcaption
      v-if="table.sarlavha"
      class="mb-1.5 text-xs font-semibold text-slate-400"
      v-text="table.sarlavha"
    />
    <div class="overflow-x-auto rounded-xl border border-line">
      <table class="min-w-full border-collapse text-sm">
        <thead>
          <tr>
            <th
              v-if="table.audio.length > 0"
              class="w-12 border-b border-line bg-ink-800"
            >
              <span class="sr-only">Tinglash</span>
            </th>
            <th
              v-for="(c, ci) in table.ustunlar"
              :key="ci"
              class="whitespace-nowrap border-b border-line bg-ink-800 px-3 py-2 text-left font-semibold text-slate-300"
              v-text="c"
            />
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(row, r) in table.qatorlar"
            :key="r"
          >
            <td
              v-if="table.audio.length > 0"
              class="border-b border-line px-1.5 py-1"
            >
              <PlayButton
                :handle="table.audio[r]"
                :label="`${r + 1}-qator`"
                small
              />
            </td>
            <td
              v-for="(cell, c) in row"
              :key="c"
              class="border-b border-line px-3 py-1.5 text-slate-200"
            >
              <ArabicText
                v-if="hasArabic(cell)"
                :text="cell"
                size="sm"
              />
              <span
                v-else
                class="whitespace-nowrap"
                v-text="cell"
              />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </figure>
</template>
