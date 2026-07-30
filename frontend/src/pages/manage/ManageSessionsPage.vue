<script setup lang="ts">
import { PageHeader } from '@/shared/ui'
import { SessionBoard } from '@/widgets/session-board'
</script>

<template>
  <div>
    <PageHeader
      title="Jonli darslar"
      subtitle="Platformadagi barcha rejalashtirilgan va jonli darslar"
    />
    <SessionBoard />
  </div>
</template>
