<script setup lang="ts">
import AppIcon from './AppIcon.vue'
import type { IconName } from './icons'

withDefaults(defineProps<{ title: string; description?: string; icon?: IconName }>(), {
  icon: 'compass',
})
</script>

<template>
  <div class="empty">
    <div class="empty__icon"><AppIcon :name="icon" :size="28" /></div>
    <h3 class="empty__title">{{ title }}</h3>
    <p v-if="description" class="empty__description">{{ description }}</p>
    <div v-if="$slots.default" class="empty__actions"><slot /></div>
  </div>
</template>

<style scoped>
.empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  gap: var(--space-3);
  padding: var(--space-7) var(--space-5);
  border: 1px dashed var(--color-border);
  border-radius: var(--radius-lg);
}
.empty__icon {
  display: grid;
  place-items: center;
  width: 56px;
  height: 56px;
  border-radius: 50%;
  color: var(--color-brass);
  background: var(--color-brass-soft);
}
.empty__title {
  font-size: var(--text-xl);
}
.empty__description {
  max-width: 42ch;
  color: var(--color-text-muted);
}
.empty__actions {
  margin-top: var(--space-2);
}
</style>
