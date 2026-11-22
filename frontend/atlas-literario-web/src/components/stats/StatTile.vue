<script setup lang="ts">
withDefaults(
  defineProps<{ label: string; value: number | string; caption?: string; accent?: boolean }>(),
  {
    accent: false,
  },
)
</script>

<template>
  <div class="tile" :class="{ 'tile--accent': accent }">
    <span class="eyebrow">{{ label }}</span>
    <span class="tile__value numeric">{{ value }}</span>
    <span v-if="caption" class="tile__caption">{{ caption }}</span>
  </div>
</template>

<style scoped>
.tile {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  padding: var(--space-4) var(--space-5);
  border-left: 1px solid var(--color-border);
}
.tile__value {
  font-family: var(--font-display);
  font-size: var(--text-3xl);
  font-weight: 400;
  line-height: 1.05;
  color: var(--color-text);
}
.tile--accent .tile__value {
  color: var(--color-brass);
}
.tile__caption {
  font-size: var(--text-xs);
  color: var(--color-text-faint);
}
</style>
