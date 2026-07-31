<script setup lang="ts">
import { PageHeader } from '@/shared/ui'
import { RecordingBoard } from '@/widgets/recording-board'

/**
 * DARS YOZUVLARI (barcha guruhlar).
 *
 * ★ SARLAVHA VA IZOH ESKI ILOVADAN AYNAN (`academic.html`, 679–681-qatorlar):
 *   <h1>Dars yozuvlari</h1>
 *   <div class="sub">Barcha guruhlarda o'tilgan jonli darslarning to'liq
 *                    video yozuvlari</div>
 * Menyudagi band ham o'sha nom bilan va o'sha o'rinda ("Testlar" dan keyin).
 */
</script>

<template>
  <div>
    <PageHeader
      title="Dars yozuvlari"
      subtitle="Barcha guruhlarda o‘tilgan jonli darslarning to‘liq video yozuvlari"
    />
    <RecordingBoard />
  </div>
</template>
