<script setup lang="ts">
withDefaults(
  defineProps<{
    tone?: 'neutral' | 'brass' | 'sky' | 'success' | 'warning' | 'danger'
    color?: string
  }>(),
  { tone: 'neutral' },
)
</script>

<template>
  <span
    class="badge"
    :class="`badge--${tone}`"
    :style="color ? { '--badge-color': color } : undefined"
  >
    <slot />
  </span>
</template>

<style scoped>
.badge {
  --badge-color: var(--color-text-muted);
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  padding: 0.15rem 0.6rem;
  border-radius: var(--radius-pill);
  font-size: var(--text-xs);
  font-weight: 600;
  letter-spacing: 0.02em;
  color: var(--badge-color);
  background: color-mix(in srgb, var(--badge-color) 14%, transparent);
  border: 1px solid color-mix(in srgb, var(--badge-color) 28%, transparent);
  white-space: nowrap;
}
.badge--brass {
  --badge-color: var(--color-brass);
}
.badge--sky {
  --badge-color: var(--color-sky);
}
.badge--success {
  --badge-color: var(--color-success);
}
.badge--warning {
  --badge-color: var(--color-warning);
}
.badge--danger {
  --badge-color: var(--color-danger);
}
</style>
