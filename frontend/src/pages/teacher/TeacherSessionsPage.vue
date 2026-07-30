<script setup lang="ts">
import { PageHeader } from '@/shared/ui'
import { SessionBoard } from '@/widgets/session-board'
</script>

<template>
  <div>
    <PageHeader
      title="Darslarim"
      subtitle="Siz olib boradigan jonli darslar. Boshlash/yakunlash dars sahifasida."
    />
    <SessionBoard />
  </div>
</template>
