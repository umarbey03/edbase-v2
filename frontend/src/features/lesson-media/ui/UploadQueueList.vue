<script setup lang="ts">
import { formatFileSize } from '@/shared/lib/text'
import { IconButton } from '@/shared/ui'

import type { UploadItem } from '../model/upload-queue'

/**
 * YUKLASH NAVBATINING KO'RINISHI — har fayl uchun ALOHIDA qator.
 *
 * Talab: *"har biri uchun progress qatori (foiz + yuklangan/jami hajm)"*.
 *
 * ★ NEGA FOIZ VA BAYTLAR IKKALASI: "42%" 1 GB faylda qancha qolganini
 * aytmaydi — 210 MB / 500 MB esa aytadi. Sekin internetda foydalanuvchi
 * aynan shu ikkinchi raqamga qarab kutish yoki bekor qilishni hal qiladi.
 *
 * ★ QATOR TUGAGANDAN KEYIN HAM QOLADI (`clearFinished` bilan tozalanadi):
 * xato matni yoki "bekor qilindi" holati yo'qolib qolsa, foydalanuvchi
 * "yuklandimi yoki yo'qmi?" degan savol bilan qolardi.
 */
const props = defineProps<{ items: readonly UploadItem[] }>()

const emit = defineEmits<{ cancel: [id: string]; retry: [id: string] }>()

/** Qator ostidagi holat matni. */
function statusText(item: UploadItem): string {
  if (item.status === 'pending') return 'Navbatda kutmoqda'
  if (item.status === 'uploading') {
    const total = item.size > 0 ? formatFileSize(item.size) : '—'
    return `${formatFileSize(item.loaded)} / ${total} · ${item.percent}%`
  }
  if (item.status === 'done') return `Yuklandi · ${formatFileSize(item.size)}`
  if (item.status === 'cancelled') return 'Bekor qilindi'
  return item.error ?? 'Yuklanmadi'
}

function statusClass(item: UploadItem): string {
  if (item.status === 'error') return 'text-rose-400'
  if (item.status === 'done') return 'text-green-400'
  return 'text-dim'
}
</script>

<template>
  <ul
    v-if="props.items.length > 0"
    class="mt-3 space-y-2"
  >
    <li
      v-for="item in props.items"
      :key="item.id"
      class="js-upload-row rounded-lg border border-line bg-ink-850 p-2.5"
    >
      <div class="flex items-start gap-3">
        <div class="min-w-0 flex-1">
          <p
            class="truncate text-[13px] font-medium text-slate-200"
            v-text="item.name"
          />
          <p
            class="mt-0.5 text-[11px] tabular-nums"
            :class="statusClass(item)"
            v-text="statusText(item)"
          />
        </div>

        <!--
          🔴 `gap-3` (12px) — `IconButton` qoidasi (13-bo'lim, 24-tuzoq):
          `tap-expand` maydoni har tomondan 6px kengayadi, kichik oraliqda
          qo'shni tugma bosilardi.
        -->
        <IconButton
          v-if="item.status === 'uploading' || item.status === 'pending'"
          icon="close"
          label="Yuklashni bekor qilish"
          size="sm"
          tone="danger"
          @click="emit('cancel', item.id)"
        />
        <IconButton
          v-else-if="item.status === 'error' || item.status === 'cancelled'"
          icon="refresh"
          label="Qaytadan yuklash"
          size="sm"
          @click="emit('retry', item.id)"
        />
      </div>

      <!--
        Progress yo'li faqat YUKLANAYOTGAN qatorda: tugagan yoki rad etilgan
        faylda to'la chiziq "hali ham davom etmoqda" degan taassurot berardi.

        `aria-valuenow` — screen reader foizni o'qiydi; `aria-label` da fayl
        nomi ham bor, aks holda uch qator progressni bir-biridan ajratib
        bo'lmasdi.
      -->
      <div
        v-if="item.status === 'uploading'"
        class="mt-2 h-1.5 overflow-hidden rounded-full bg-ink-750"
        role="progressbar"
        :aria-label="`${item.name} yuklanmoqda`"
        aria-valuemin="0"
        aria-valuemax="100"
        :aria-valuenow="item.percent"
      >
        <div
          class="h-full rounded-full bg-brand-500 transition-[width] duration-200 motion-reduce:transition-none"
          :style="{ width: `${item.percent}%` }"
        />
      </div>
    </li>
  </ul>
</template>
