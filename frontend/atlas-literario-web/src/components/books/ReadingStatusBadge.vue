<script setup lang="ts">
import type { ReadingStatus } from '@/types/api'
import { READING_STATUS_LABELS } from '@/utils/labels'
import BaseBadge from '@/components/ui/BaseBadge.vue'

defineProps<{ status: ReadingStatus }>()

const COLORS: Record<ReadingStatus, string> = {
  WantToRead: 'var(--status-want)',
  Reading: 'var(--status-reading)',
  Read: 'var(--status-read)',
  Abandoned: 'var(--status-abandoned)',
}
</script>

<template>
  <BaseBadge :color="COLORS[status]">{{ READING_STATUS_LABELS[status] }}</BaseBadge>
</template>
